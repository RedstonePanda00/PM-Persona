using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PMP_Persona.ShipSupport
{
    public class ShipSupportCost
    {
        public ThingDef thingDef;
        public int count;
    }

    public class ShipSupportDef : Def
    {
        public Type workerClass;
        public int order;
        public string iconPath;
        public string backgroundPath;
        public string effectDescription;
        public ThingDef activeThingDef;
        public List<ShipSupportCost> costList = new List<ShipSupportCost>();

        private ShipSupportWorker workerInt;
        private Texture2D iconInt;
        private Texture2D backgroundInt;

        public ShipSupportWorker Worker
        {
            get
            {
                if (workerInt == null && workerClass != null)
                {
                    workerInt = (ShipSupportWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public Texture2D Icon
        {
            get
            {
                if (iconInt == null && !iconPath.NullOrEmpty())
                    iconInt = ContentFinder<Texture2D>.Get(iconPath, false);
                return iconInt ?? BaseContent.BadTex;
            }
        }

        public Texture2D Background
        {
            get
            {
                if (backgroundInt == null && !backgroundPath.NullOrEmpty())
                    backgroundInt = ContentFinder<Texture2D>.Get(backgroundPath, false);
                return backgroundInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
                yield return error;

            if (workerClass == null)
                yield return "workerClass is required";
            else if (!typeof(ShipSupportWorker).IsAssignableFrom(workerClass))
                yield return "workerClass must extend ShipSupportWorker";

            if (iconPath.NullOrEmpty())
                yield return "iconPath is required";

            if (costList == null)
                yield break;
            for (int i = 0; i < costList.Count; i++)
            {
                ShipSupportCost cost = costList[i];
                if (cost == null || cost.thingDef == null)
                    yield return "costList contains a null cost or ThingDef";
                else if (cost.count <= 0)
                    yield return "costList count must be greater than zero";
            }
        }
    }
}
