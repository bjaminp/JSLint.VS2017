using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSLint.VS2017
{
    static class GuidList
    {
        public const string guidPkgString = "531da20f-aec5-4382-a317-0f70a217a572";
        public const string guidSourceEditorCmdSetString = "36c9360d-beb4-4ee0-9e7c-75264224c59c";

        public const string guidSourceEditorFragmentCmdSetString = "45b15f14-e401-462c-8eb0-5fcab4cc3195";
        public const string guidSolutionItemCmdSetString = "d9032052-cbd8-4f67-8352-e183f74e4071";
        public const string guidSolutionFolderNodeCmdSetString = "11ecda11-e5ee-4e59-a4e1-ef9edec80d8f";
        public const string guidOptionsCmdSetString = "b7fe589c-403a-4bfa-88e2-795a77b2d5b3";
        public const string guidErrorListCmdString = "284b5171-6b47-4cbf-a617-ab8b7d113725";
        public const string guidSolutionNodeCmdString = "72a924bc-cf95-456b-bc68-edf0b2022c7d";


        public static readonly Guid guidSourceEditorCmdSet = new Guid(guidSourceEditorCmdSetString);
        public static readonly Guid guidSourceEditorFragmentCmdSet = new Guid(guidSourceEditorFragmentCmdSetString);
        public static readonly Guid guidSolutionItemCmdSet = new Guid(guidSolutionItemCmdSetString);
        public static readonly Guid guidSolutionFolderNodeCmdSet = new Guid(guidSolutionFolderNodeCmdSetString);
        public static readonly Guid guidOptionsCmdSet = new Guid(guidOptionsCmdSetString);
        public static readonly Guid guidErrorListCmdSet = new Guid(guidErrorListCmdString);
        public static readonly Guid guidSolutionNodeCmdSet = new Guid(guidSolutionNodeCmdString);
    }

    static class PkgCmdIDList
    {
        public const uint lint = 0x100;
        public const uint options = 0x100;
        public const uint wipeerrors = 0x100;
        public const uint exclude = 0x101;
        public const uint excludeFolder = 0x100;
        public const uint globals = 0x102;
        public const uint addconfig = 0x103;
        public const uint editconfig = 0x104;
        public const uint removeconfig = 0x105;
    }
}
