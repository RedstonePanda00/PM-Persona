using System;
using System.IO;
using System.Linq;
using RimCampaign.Core;

// Read-only validation of the latest hand-authored project. Never regenerate maps.
static class Program
{
    static int Main(string[] args)
    {
        try {
            if(args.Length>1)throw new ArgumentException("Usage: MissionOne.dll [repository root]");
            string root=Path.GetFullPath(args.Length==1?args[0]:".");
            string path=Path.Combine(root,"Campaigns","MissionOne.project.json.gz");
            var project=Json.Load<Project>(path);
            var errors=Validation.Check(project).ToList();
            if(errors.Any())throw new Exception(string.Join("\n",errors));
            MissionTests.Run(project);
            MissionTwoObjectiveTests.Run(project);
            MissionFourTests.Run(project);
            Console.WriteLine("PASS read-only authoring validation: "+path);
            return 0;
        } catch(Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
