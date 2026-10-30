#version 450
#define PM_VERTEX
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } uVertex;
layout(set = 2, binding = 0) uniform MaterialUniforms {
    vec4 _ChargeColor; // @color @default = vec4(0.20000000298023224, 0.5, 1.0, 1.0)
    vec4 _CoreColor; // @color @default = vec4(1.0, 0.30000001192092896, 0.10000000149011612, 1.0)
    vec4 _GlowColor; // @color @default = vec4(1.0, 0.10000000149011612, 0.05000000074505806, 1.0)
    vec4 _Time;
    float _CoreWidth; // @default = 0.07999999821186066
    float _EdgeSoftness; // @default = 0.11999999731779099
    float _FlowSpeed; // @default = 3.0
    float _GlowIntensity; // @default = 4.0
    float _GlowWidth; // @default = 0.3499999940395355
    float _LifePhase; // @default = 0.5
    float _NoiseOctaves; // @default = 3.0
    float _NoiseScale; // @default = 3.0
    float _NoiseStrength; // @default = 0.25
    float _PulseAmplitude; // @default = 0.15000000596046448
    float _PulseSpeed; // @default = 6.0
} uMaterial;

#define UNITY_PI 3.14159265358979323846
#define UNITY_TWO_PI 6.28318530717958647692
#define saturate(x) clamp(x, 0.0, 1.0)
#define rsqrt(x) inversesqrt(x)
#define fmod(x,y) ((x) - (y) * trunc((x)/(y)))
#define TRANSFORM_TEX(uv,tex) ((uv).xy * tex##_ST.zw + tex##_ST.xy)
#define mul(a,b) ((a)*(b))
#define unity_ObjectToWorld push.modelMatrix
vec4 UnityObjectToClipPos(vec4 p) { return uVertex.ViewProj * push.modelMatrix * p; }
vec4 UnityObjectToClipPos(vec3 p) { return UnityObjectToClipPos(vec4(p,1.0)); }
vec4 ComputeGrabScreenPos(vec4 p) { return vec4(p.xy * .5 + p.ww * .5, p.zw); }
vec4 PMTexture(sampler2D image, vec2 uv) { return texture(image,vec2(uv.x,1.0-uv.y)); }
void PMSincos(float a, out float s, out float c) {s=sin(a);c=cos(a);}
float distance(vec2 p, float q) {return length(p-vec2(q));}
#ifdef PM_VERTEX
#define fwidth(x) ((x)*0.0)
#define dFdx(x) ((x)*0.0)
#define dFdy(x) ((x)*0.0)
void clip(float v) {}
#else
// Use coarse derivatives to match Unity's Vulkan fwidth/ddx/ddy.
#define fwidth(x) (abs(dFdxCoarse(x)) + abs(dFdyCoarse(x)))
#define dFdx(x) dFdxCoarse(x)
#define dFdy(x) dFdyCoarse(x)
void clip(float v) {if(v<0.0)discard;}
#endif



            struct appdata { vec4 vertex ; vec2 uv ; };
            struct v2f     { vec4 pos    ; vec2 uv ; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float hash(vec2 p)
            {
                return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
            }

            float valueNoise(vec2 p)
            {
                vec2 i = floor(p);
                vec2 f = fract(p);
                vec2 u = f * f * (3.0 - 2.0 * f); 

                float a = hash(i + vec2(0,0));
                float b = hash(i + vec2(1,0));
                float c = hash(i + vec2(0,1));
                float d = hash(i + vec2(1,1));

                return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
            }

            float fbm(vec2 p, int octaves)
            {
                float val    = 0.0;
                float amp    = 0.5;
                float freq   = 1.0;
                float maxVal = 0.0;

                for (int o = 0; o < 4; o++)   
                {
                    if (o >= octaves) break;
                    val    += valueNoise(p * freq) * amp;
                    maxVal += amp;
                    amp    *= 0.5;
                    freq   *= 2.1;
                }
                return val / maxVal;  
            }

            float radialMask(float uvY, float halfWidth)
            {
                float d = abs(uvY - 0.5);
                return 1.0 - smoothstep(0.0, halfWidth, d);
            }

            float edgeFade(float uvX, float softness)
            {
                return smoothstep(0.0, softness, uvX) * smoothstep(1.0, 1.0 - softness, uvX);
            }

            vec3 lifeWeights(float phase)
            {
                float chargeW   = 1.0 - smoothstep(0.0,  0.33, phase);
                float fireW     = smoothstep(0.0, 0.33, phase) * (1.0 - smoothstep(0.66, 1.0, phase));
                float dissolveW = smoothstep(0.66, 1.0, phase);
                return vec3(chargeW, fireW, dissolveW);
            }

            vec4 frag(v2f i) 
            {
                vec2 uv = i.uv;

                float fade = edgeFade(uv.x, uMaterial._EdgeSoftness);

                vec2 noiseUV = vec2(uv.x * uMaterial._NoiseScale - uMaterial._Time.y * uMaterial._FlowSpeed, uv.y * 2.0);
                float  noise   = fbm(noiseUV, int(uMaterial._NoiseOctaves));

                float pulse = 1.0 + uMaterial._PulseAmplitude * sin(uMaterial._Time.y * uMaterial._PulseSpeed + uv.x * 6.2831);

                float noisedY = uv.y + (noise - 0.5) * uMaterial._NoiseStrength;
                float coreM   = radialMask(noisedY, uMaterial._CoreWidth);
                float glowM   = radialMask(uv.y,    uMaterial._GlowWidth);

                vec3 lw = lifeWeights(uMaterial._LifePhase);

                float chargeScale  = clamp(uMaterial._LifePhase / 0.33, 0.0, 1.0);
                float dissolveAlpha = 1.0 - smoothstep(0.66, 1.0, uMaterial._LifePhase);

                vec4 coreCol  = uMaterial._CoreColor  * coreM * pulse * uMaterial._GlowIntensity;
                vec4 glowCol  = uMaterial._GlowColor  * glowM * uMaterial._GlowIntensity * 0.4;
                vec4 fireCol  = (coreCol + glowCol) * fade;
                vec4 chargeCol = uMaterial._ChargeColor * coreM * chargeScale * uMaterial._GlowIntensity * 0.6 * fade;

                vec4 col = fireCol    * lw.y
                           + chargeCol  * lw.x
                           + fireCol    * dissolveAlpha * lw.z;

                col.rgb += noise * uMaterial._NoiseStrength * coreM * fade * pulse;

                col *= mix(1.0, dissolveAlpha, lw.z);

                return col;
            }
            
layout(location = 0) in vec3 pmInvertex; // @vertex Position required
layout(location = 3) in vec2 pmInuv; // @vertex TexCoord0 optional @default = vec4(0.0)
layout(location = 0) out vec2 pmVuv;
void main() {
appdata pmInput;
pmInput.vertex = vec4(pmInvertex, 1.0);
pmInput.uv = pmInuv;
v2f pmOutput = vert(pmInput);
gl_Position = pmOutput.pos;
pmVuv = pmOutput.uv;
}
