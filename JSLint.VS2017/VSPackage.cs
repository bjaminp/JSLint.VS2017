//------------------------------------------------------------------------------
// <copyright file="VSPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using JSLint.Framework;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using JSLint.Framework.LinterBridge;
using JSLint.Framework.OptionClasses;
using JSLint.Framework.OptionClasses.OptionProviders;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using JSLint.UI.OptionsUI;
using Microsoft.VisualStudio.Editor;

namespace JSLint.VS2017
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    //[ProvideAutoLoad(UIContextGuids80.NoSolution)]
    //[ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Guid(GuidList.guidPkgString)]
    [ProvideSolutionProps(VSPackage.SolutionPropertiesKeyName)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class VSPackage : Package, IVsPersistSolutionProps, IDisposable
    {
        /// <summary>
        /// VSPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "28d42969-004f-481f-819b-65b4e1e90691";

        private JSLinter _linter = new JSLinter();
        private ErrorListHelper _errorListHelper;
        private DTE2 _dte2;
        private SolutionEvents _solutionEvents; 
        private BuildEvents _buildEvents;
        private DocumentEvents _docEvents;
        private bool _usingSolutionOptions = false;
        private bool _hasDirtySolutionProperties = false;
        private string _solutionConfigurationPath;
        private const string SolutionPropertiesKeyName = "JSLint";
        private static readonly string JsLintOptionsFileName = "JSLintOptions.xml";
        private static readonly string GlobalOptionsPath = Utility.GetFilename(JsLintOptionsFileName);
        private static readonly char[] DirectorySeparators = { Path.DirectorySeparatorChar };
        private vsBuildScope _buildScope;
        private vsBuildAction _buildAction;
        private int _errorCount;
        private const int Threshold = 1000;
        private readonly Dictionary<string, List<string>> _skippedNodes = new Dictionary<string, List<string>>(8);

        /// <summary>
        /// Initializes a new instance of the <see cref="VSPackage"/> class.
        /// </summary>
        public VSPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            OptionsProviderRegistry.PushOptionsProvider(new FileOptionsProvider("Global", GlobalOptionsPath));
            OptionsProviderRegistry.ReloadCurrent();

            _dte2 = GetService(typeof(DTE)) as DTE2;
            _solutionEvents = _dte2.Events.SolutionEvents;
            _buildEvents = _dte2.Events.BuildEvents;
            _docEvents = _dte2.Events.DocumentEvents;
            _errorListHelper = new ErrorListHelper(this);

            // Add our command handlers for menu (commands must exist in the .vsct file)
            
            var mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                // Source Editor: JS Lint
                var sourceEditorLintCmdId = new CommandID(GuidList.guidSourceEditorCmdSet, (int)PkgCmdIDList.lint);
                var sourceEditorLintMenuItem = new MenuCommand(LintSourceEditorCmdCallback, sourceEditorLintCmdId);
                mcs.AddCommand(sourceEditorLintMenuItem);

                // Source Editor: JS Lint Fragment
                var sourceEditorFragmentLintCmdId = new CommandID(GuidList.guidSourceEditorFragmentCmdSet, (int)PkgCmdIDList.lint);
                var sourceEditorFragmentLintMenuItem = new OleMenuCommand(LintSourceEditorFragmentItemCmdCallback, sourceEditorFragmentLintCmdId);
                sourceEditorFragmentLintMenuItem.BeforeQueryStatus += SourceEditorFragmentLintMenuItem_BeforeQueryStatus;
                mcs.AddCommand(sourceEditorFragmentLintMenuItem);

                // Solution Explorer: JS Lint
                var solutionItemCmdId = new CommandID(GuidList.guidSolutionItemCmdSet, (int)PkgCmdIDList.lint);
                var solutionItemMenuItem = new OleMenuCommand(LintSolutionItemCmdCallback, solutionItemCmdId);
                solutionItemMenuItem.BeforeQueryStatus += SolutionItemMenuItem_BeforeQueryStatus;
                mcs.AddCommand(solutionItemMenuItem);

                // Solution Explorer: Skip File
                var solutionItemSkipCmdId = new CommandID(GuidList.guidSolutionItemCmdSet, (int)PkgCmdIDList.exclude);
                var solutionItemSkipMenuItem = new OleMenuCommand(LintSolutionItemSkipCmdCallback, solutionItemSkipCmdId);
                solutionItemSkipMenuItem.BeforeQueryStatus += SolutionItemSkipMenuItem_BeforeQueryStatus;
                mcs.AddCommand(solutionItemSkipMenuItem);

                // Solution Explorer: Skip Folder
                var solutionFolderNodeSkipCmdId = new CommandID(GuidList.guidSolutionFolderNodeCmdSet, (int)PkgCmdIDList.excludeFolder);
                var solutionFolderNodeSkipMenuItem = new OleMenuCommand(LintSolutionFolderNodeSkipCmdCallback, solutionFolderNodeSkipCmdId);
                solutionFolderNodeSkipMenuItem.BeforeQueryStatus += SolutionFolderNodeSkipMenuItem_BeforeQueryStatus;
                mcs.AddCommand(solutionFolderNodeSkipMenuItem);

                // Solution Explorer: Predefined global variables
                var solutionItemGlobalsCmdId = new CommandID(GuidList.guidSolutionItemCmdSet, (int)PkgCmdIDList.globals);
                var solutionItemGlobalsMenuItem = new OleMenuCommand(LintSolutionItemGlobalsCmdCallback, solutionItemGlobalsCmdId);
                solutionItemGlobalsMenuItem.BeforeQueryStatus += SolutionItemGlobalsMenuItem_BeforeQueryStatus;
                mcs.AddCommand(solutionItemGlobalsMenuItem);

                // Source Editor: Predefined global variables
                var sourceEditorGlobalsCmdId = new CommandID(GuidList.guidSourceEditorCmdSet, (int)PkgCmdIDList.globals);
                var sourceEditorGlobalsMenuItem = new MenuCommand(LintSourceEditorGlobalsCmdCallback, sourceEditorGlobalsCmdId);
                mcs.AddCommand(sourceEditorGlobalsMenuItem);

                // Error List: Clear JS Lint Errors
                var errorListCmdId = new CommandID(GuidList.guidErrorListCmdSet, (int)PkgCmdIDList.wipeerrors);
                var errorListMenuItem = new OleMenuCommand(ErrorListCmdCallback, errorListCmdId);
                errorListMenuItem.BeforeQueryStatus += ErrorListMenuItem_BeforeQueryStatus;
                mcs.AddCommand(errorListMenuItem);

                // Solution Node: Add Config
                var addConfigCmdId = new CommandID(GuidList.guidSolutionNodeCmdSet, (int)PkgCmdIDList.addconfig);
                var addConfigMenuItem = new OleMenuCommand(AddSolutionOptionsFileCmdCallback, addConfigCmdId);
                addConfigMenuItem.BeforeQueryStatus += addConfigMenuItem_BeforeQueryStatus;
                mcs.AddCommand(addConfigMenuItem);

                // Solution Node: Edit Config
                var editConfigCmdId = new CommandID(GuidList.guidSolutionNodeCmdSet, (int)PkgCmdIDList.editconfig);
                var editConfigMenuItem = new OleMenuCommand(EditSolutionOptionsFileCmdCallback, editConfigCmdId);
                editConfigMenuItem.BeforeQueryStatus += editOrRemoveConfigMenuItem_BeforeQueryStatus;
                mcs.AddCommand(editConfigMenuItem);

                // Solution Node: Remove Config
                var removeConfigCmdId = new CommandID(GuidList.guidSolutionNodeCmdSet, (int)PkgCmdIDList.removeconfig);
                var removeConfigMenuItem = new OleMenuCommand(RemoveSolutionOptionsFileCmdCallback, removeConfigCmdId);
                removeConfigMenuItem.BeforeQueryStatus += editOrRemoveConfigMenuItem_BeforeQueryStatus;
                mcs.AddCommand(removeConfigMenuItem);

                // Main Menu: JSLint Options
                var optionsCmdId = new CommandID(GuidList.guidOptionsCmdSet, (int)PkgCmdIDList.options);
                var optionsMenuItem = new MenuCommand(OptionsCmdCallback, optionsCmdId);
                mcs.AddCommand(optionsMenuItem);
            }

            //solution events
            _solutionEvents.Opened += solutionEvents_Opened;
            _solutionEvents.AfterClosing += SolutionEvents_AfterClosing;

            // build events
            _buildEvents.OnBuildBegin += buildEvents_OnBuildBegin;
            _buildEvents.OnBuildProjConfigBegin += buildEvents_OnBuildProjConfigBegin;

            //document events
            _docEvents.DocumentSaved += DocumentEvents_DocumentSaved;
            base.Initialize();
        }

        #endregion

        #region IVsPersistSolutionProps Members

        public int QuerySaveSolutionProps([In] IVsHierarchy pHierarchy, [Out] VSQUERYSAVESLNPROPS[] pqsspSave)
        {
            VSQUERYSAVESLNPROPS state = VSQUERYSAVESLNPROPS.QSP_HasNoProps;
            bool isSolutionFile = (pHierarchy == null);
            if (isSolutionFile)
            {
                if (_usingSolutionOptions)
                {
                    if (_hasDirtySolutionProperties)
                    {
                        state = VSQUERYSAVESLNPROPS.QSP_HasDirtyProps;
                    }
                    else
                    {
                        state = VSQUERYSAVESLNPROPS.QSP_HasNoDirtyProps;
                    }
                }
            }

            pqsspSave[0] = state;

            return VSConstants.S_OK;
        }

        private const string SolutionConfigurationLocationProperty = "SolutionConfigurationLocation";

        public int SaveSolutionProps([In] IVsHierarchy pHierarchy, [In] IVsSolutionPersistence pPersistence)
        {
            const int writeToPreLoadSection = 1; //1 == true
            pPersistence.SavePackageSolutionProps(writeToPreLoadSection, pHierarchy, this, SolutionPropertiesKeyName);

            _hasDirtySolutionProperties = false;
            return VSConstants.S_OK;
        }

        public int ReadSolutionProps([In] IVsHierarchy pHierarchy, [In] string pszProjectName,
        [In] string pszProjectMk, [In] string pszKey, [In] int fPreLoad,
        [In] IPropertyBag pPropBag)
        {
            object solutionConfigurationLocation;
            pPropBag.Read(SolutionConfigurationLocationProperty, out solutionConfigurationLocation, null, 0, null);

            _solutionConfigurationPath = Path.Combine(Path.GetDirectoryName(_dte2.Solution.FullName), (string)solutionConfigurationLocation);

            _usingSolutionOptions = true;

            return VSConstants.S_OK;
        }

        public int WriteSolutionProps([In] IVsHierarchy pHierarchy, [In] string pszKey, [In] IPropertyBag pPropBag)
        {
            if (_usingSolutionOptions)
            {
                string value = _solutionConfigurationPath
                        .Substring(Path.GetDirectoryName(_dte2.Solution.FullName).Length)
                        .TrimStart(DirectorySeparators);
                pPropBag.Write(SolutionConfigurationLocationProperty, value);
            }
            return VSConstants.S_OK;
        }

        public int ReadUserOptions([In] IStream pOptionsStream, [In] string pszKey)
        {
            return VSConstants.S_OK;
        }

        public int SaveUserOptions([In] IVsSolutionPersistence pPersistence)
        {
            return VSConstants.S_OK;
        }

        public int WriteUserOptions([In] IStream pOptionsStream, [In] string pszKey)
        {
            return VSConstants.S_OK;
        }

        public int LoadUserOptions([In] IVsSolutionPersistence pPersistence, [In] uint grfLoadOpts)
        {
            return VSConstants.S_OK;
        }

        public int OnProjectLoadFailure([In] IVsHierarchy pStubHierarchy, [In] string pszProjectName, [In] string pszProjectMk,
            [In] string pszKey)
        {
            return VSConstants.S_OK;
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (_errorListHelper != null)
            {
                _errorListHelper.Dispose();
                _errorListHelper = null;
            }

            if (_linter != null)
            {
                _linter.Dispose();
                _linter = null;
            }
        }

        #endregion

        #region EventHandlers

        private void addConfigMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            OleMenuCommand menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                menuCommand.Visible = !_usingSolutionOptions;
            }
        }

        private void editOrRemoveConfigMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            OleMenuCommand menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                menuCommand.Visible = _usingSolutionOptions;
            }
        }

        private void solutionEvents_Opened()
        {
            if (_usingSolutionOptions)
            {
                if (File.Exists(_solutionConfigurationPath))
                {
                    var newProvider = new FileOptionsProvider("Solution", _solutionConfigurationPath);
                    OptionsProviderRegistry.PushOptionsProvider(newProvider);
                    _usingSolutionOptions = true;
                }
                else
                {
                    _usingSolutionOptions = false;
                }
            }
        }

        private void SolutionEvents_AfterClosing()
        {
            if (_usingSolutionOptions)
            {
                OptionsProviderRegistry.PopOptionsProvider();
            }
            _usingSolutionOptions = false;
        }

        private void DocumentEvents_DocumentSaved(Document document)
        {
            var currentOptions = OptionsProviderRegistry.CurrentOptions;
            if (currentOptions.RunOnSave && document != null)
            {
                var fileType = GetFileType(document.FullName);
                if ((fileType & currentOptions.SaveFileTypes) > 0)
                {
                    ResetErrorCount();
                    SuspendErrorList();

                    bool bDontCare;
                    ClearErrors(document.FullName);
                    AnalyzeFile(document.FullName, out bDontCare);

                    ResumeErrorList(false);
                }
            }
        }

        private void SolutionItemMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                menuCommand.Visible = ActiveUiHierarchyItems.Cast<UIHierarchyItem>()
                                                            .Select(item => GetFileType(item.Name))
                                                            .All(type => type != IncludeFileType.None && type != IncludeFileType.Folder);
            }
        }

        private void SolutionItemSkipMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            OleMenuCommand menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                var activeItems = ActiveUiHierarchyItems.Cast<UIHierarchyItem>();
                var buildFileTypes = OptionsProviderRegistry.CurrentOptions.BuildFileTypes;

                menuCommand.Visible = activeItems.Select(item => GetFileType(item.Name)).All(type => buildFileTypes.HasFlag(type) && (type != IncludeFileType.None));
                menuCommand.Checked = false;
                menuCommand.Enabled = true;
                foreach (var item in activeItems)
                {
                    ProjectItem projItem = (ProjectItem)item.Object;
                    if (SolutionFolderKind.Equals(projItem.ContainingProject.Kind, StringComparison.Ordinal))
                    {
                        menuCommand.Visible = false;
                        return;
                    }
                    bool skippedByFolder;
                    if (IsNodeSkipped(projItem, out skippedByFolder))
                    {
                        menuCommand.Checked = true;
                        if (skippedByFolder)
                        {
                            menuCommand.Enabled = false;
                        }

                        return;
                    }
                }
            }
        }

        private void SolutionFolderNodeSkipMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                menuCommand.Visible = true;
                var activeItems = ActiveUiHierarchyItems.Cast<UIHierarchyItem>();
                menuCommand.Checked = activeItems.Select(item => (ProjectItem)item.Object).Any(IsNodeSkipped);
            }
        }

        private void SolutionItemGlobalsMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                var activeItems = ActiveUiHierarchyItems;
                if (activeItems.Length != 1)
                {
                    menuCommand.Visible = false;

                    return;
                }

                menuCommand.Visible = GetFileType(((ProjectItem)ActiveUiHierarchyItem.Object).Name) == IncludeFileType.JS;
            }
        }

        private void SourceEditorFragmentLintMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                string selection = ActiveTextDocument.Selection.Text;

                menuCommand.Visible = selection.Length > 0 && (GetFileType(_dte2.ActiveDocument.FullName, selection) != IncludeFileType.None);
            }
        }

        private void ErrorListMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            OleMenuCommand menuCommand = sender as OleMenuCommand;
            var currentOptions = OptionsProviderRegistry.CurrentOptions;
            if (menuCommand != null)
            {
                menuCommand.Visible = currentOptions.ErrorCategory.IsTaskError() &&
                    _errorListHelper != null &&
                    _errorListHelper.ErrorCount > 0;
            }
        }

        private void EditSolutionOptionsFileCmdCallback(object sender, EventArgs e)
        {
            OpenOptionsDialog();
        }

        private void RemoveSolutionOptionsFileCmdCallback(object sender, EventArgs e)
        {
            var projItem = _dte2.Solution.FindProjectItem(_solutionConfigurationPath);
            projItem?.Remove();

            OptionsProviderRegistry.PopOptionsProvider();
            _usingSolutionOptions = false;
            _solutionConfigurationPath = null;
            _hasDirtySolutionProperties = true;
        }

        private static string GetFileName(ProjectItem projItem)
        {
            //get round some really weird 2012 behaviour
            // where Filenames[0] causes an exception
            // and Filenames[1] does not..
            // to reproduce add a solution folder and put a js file in it.
            try
            {
                return projItem.FileNames[1];
            }
            catch
            {
                try
                {
                    return projItem.FileNames[0];
                }
                catch
                {
                }
            }
            return "";
        }

        private void LintSolutionItemCmdCallback(object sender, EventArgs e)
        {
            ResetErrorCount();
            SuspendErrorList();

            foreach (var item in ActiveUiHierarchyItems)
            {
                ProjectItem projItem = (ProjectItem)((UIHierarchyItem)item).Object;
                var fileName = GetFileName(projItem);

                ClearErrors(fileName);
                bool reachedTreshold;
                AnalyzeFile(fileName, out reachedTreshold);
                if (reachedTreshold)
                {
                    break;
                }
            }

            ResumeErrorList();
        }

        private void LintSolutionItemSkipCmdCallback(object sender, EventArgs e)
        {
            var buildFileTypes = OptionsProviderRegistry.CurrentOptions.BuildFileTypes;

            var buildableFiles = ActiveUiHierarchyItems.Cast<UIHierarchyItem>()
                                                    .Select(item => item.Object)
                                                    .Cast<ProjectItem>()
                                                    .Select(projItem => new
                                                    {
                                                        Item = projItem,
                                                        FileType = GetFileType(projItem.Name)
                                                    })
                                                    .Where(c => buildFileTypes.HasFlag(c.FileType))
                                                    .Select(c => c.Item);

            foreach (var item in buildableFiles)
            {
                ToggleSkip(item);
            }
        }

        private void LintSolutionFolderNodeSkipCmdCallback(object sender, EventArgs e)
        {
            var activeProjectItems = ActiveUiHierarchyItems.Cast<UIHierarchyItem>()
                                                            .Select(item => item.Object)
                                                            .Cast<ProjectItem>();
            foreach (var projItem in activeProjectItems)
            {
                ToggleSkip(projItem);
            }
        }

        private static void GotoGlobals(TextDocument doc)
        {
            doc.Selection.MoveToLineAndOffset(1, 1);
            if (doc.Selection.FindText("/*global"))
            {
                doc.Selection.SelectLine();
            }
            else
            {
                doc.CreateEditPoint().Insert("/*global _comma_separated_list_of_variables_*/\r\n");
                doc.Selection.MoveToLineAndOffset(1, 1);
                doc.Selection.FindText("_comma_separated_list_of_variables_");
            }
        }

        private void LintSolutionItemGlobalsCmdCallback(object sender, EventArgs e)
        {
            var item = ActiveUiHierarchyItem;
            var projItem = (ProjectItem)item.Object;
            projItem.Open().Document.Activate();

            GotoGlobals(ActiveTextDocument);
        }

        private void LintSourceEditorGlobalsCmdCallback(object sender, EventArgs e)
        {
            GotoGlobals(ActiveTextDocument);
        }

        private void LintSourceEditorFragmentItemCmdCallback(object sender, EventArgs e)
        {
            if (ActiveTextDocument.Selection.Text.Length > 0)
            {
                SuspendErrorList();

                var filename = _dte2.ActiveDocument.FullName;
                ClearErrors(filename);

                AnalyzeFragment(ActiveTextDocument.Selection.Text, filename, ActiveTextDocument.Selection.TopPoint.Line, ActiveTextDocument.Selection.TopPoint.DisplayColumn);

                ResumeErrorList();
            }
        }

        private void LintSourceEditorCmdCallback(object sender, EventArgs e)
        {
            SuspendErrorList();

            var view = GetActiveTextView();
            ITextDocument document;
            view.TextDataModel.DocumentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out document);
            var filename = document != null ? document.FilePath : "";

            ClearErrors(filename);

            string selectionText = ActiveTextDocument.Selection.Text;

            if (!String.IsNullOrWhiteSpace(selectionText))
            {
                AnalyzeFragment(selectionText, filename, ActiveTextDocument.Selection.TopPoint.Line, ActiveTextDocument.Selection.TopPoint.DisplayColumn);
            }
            else
            {
                string lintFile = view.TextBuffer.CurrentSnapshot.GetText();

                AnalyzeFragment(lintFile, filename);
            }

            ResumeErrorList();
        }

        private void ErrorListCmdCallback(object sender, EventArgs e)
        {
            if (OptionsProviderRegistry.CurrentOptions.ErrorCategory.IsTaskError())
            {
                if (_errorListHelper != null)
                {
                    _errorListHelper.Clear();
                    ResetErrorCount();
                }
            }
        }

        private void AddSolutionOptionsFileCmdCallback(object sender, EventArgs e)
        {
            _solutionConfigurationPath = GetDefaultSolutionConfigurationPath();
            EnsureSolutionOptionsFileCreated();
            AddSolutionOptionsFileToSolutionItems();
            var newProvider = ConstructSolutionConfigurationProvider();
            OptionsProviderRegistry.PushOptionsProvider(newProvider);

            _hasDirtySolutionProperties = true;
            _usingSolutionOptions = true;
        }

        private FileOptionsProvider ConstructSolutionConfigurationProvider()
        {
            var currentOptions = OptionsProviderRegistry.CurrentOptions;
            var newProvider = new FileOptionsProvider("Solution", _solutionConfigurationPath);
            if (!File.Exists(_solutionConfigurationPath))
            {
                newProvider.Save(currentOptions);
            }
            return newProvider;
        }

        private void AddSolutionOptionsFileToSolutionItems()
        {
            _dte2.ItemOperations.AddExistingItem(_solutionConfigurationPath);
        }

        private void EnsureSolutionOptionsFileCreated()
        {
            bool solutionConfigExists = File.Exists(_solutionConfigurationPath);
            if (!solutionConfigExists)
            {
                using (FileStream fstream = File.Open(_solutionConfigurationPath, FileMode.Create))
                {
                    OptionsSerializer serializer = new OptionsSerializer();
                    serializer.Serialize(fstream, OptionsProviderRegistry.CurrentOptions);
                    fstream.Flush();
                }
            }
        }

        private string GetDefaultSolutionConfigurationPath()
        {
            var dirName = Path.GetDirectoryName(_dte2.Solution.FullName);
            var fullyQualifiedPath = Path.Combine(dirName, JsLintOptionsFileName);
            return fullyQualifiedPath;
        }

        private void OptionsCmdCallback(object sender, EventArgs e)
        {
            OpenOptionsDialog();
        }

        private void OpenOptionsDialog()
        {
            using (var optionsForm = new OptionsForm())
            {
                optionsForm.OptionsSourceName = _usingSolutionOptions ? "(This Solution)" : "";
                optionsForm.ShowDialog();
            }
        }

        private void buildEvents_OnBuildBegin(vsBuildScope Scope, vsBuildAction Action)
        {
            _buildScope = Scope;
            _buildAction = Action;

            _errorListHelper?.Clear();
        }

        private void buildEvents_OnBuildProjConfigBegin(
            string Project,
            string ProjectConfig,
            string Platform,
            string SolutionConfig)
        {
            Options currentOptions = OptionsProviderRegistry.CurrentOptions;
            if (_buildAction == vsBuildAction.vsBuildActionClean ||
                !currentOptions.Enabled ||
                !currentOptions.RunOnBuild)
            {
                return;
            }

            var proj = _dte2.Solution.AllProjects().Single(p => p.UniqueName == Project);

            ResetErrorCount();
            SuspendErrorList();

            bool reachedTreshold;
            AnalyzeProjectItems(proj.ProjectItems, out reachedTreshold);

            ResumeErrorList();
            UpdateStatusBar(reachedTreshold);


            if (_errorCount > 0 && currentOptions.CancelBuildOnError)
            {
                WriteToErrorList("Build cancelled due to JSLint validation errors.");
                _dte2.ExecuteCommand("Build.Cancel");
            }
        }

        private TextDocument ActiveTextDocument => (TextDocument)_dte2.ActiveDocument.Object("TextDocument");

        private IWpfTextView GetActiveTextView()
        {
            IWpfTextView view = null;
            var txtMgr = (IVsTextManager)GetService(typeof(SVsTextManager));
            const int mustHaveFocus = 1;
            txtMgr.GetActiveView(mustHaveFocus, null, out IVsTextView vTextView);

            var userData = vTextView as IVsUserData;

            if (null != userData)
            {
                var guidViewHost = DefGuidList.guidIWpfTextViewHost;
                userData.GetData(ref guidViewHost, out object holder);
                var viewHost = (IWpfTextViewHost)holder;
                view = viewHost.TextView;
            }
            return view;
        }

        private Array ActiveUiHierarchyItems
        {
            get
            {
                var h = (UIHierarchy)_dte2.ToolWindows.GetToolWindow(EnvDTE.Constants.vsWindowKindSolutionExplorer);
                return (Array)h.SelectedItems;
            }
        }

        private UIHierarchyItem ActiveUiHierarchyItem => (UIHierarchyItem)ActiveUiHierarchyItems.GetValue(0);

        private IVsBuildPropertyStorage GetVsBuildPropertyStorage(Project proj)
        {
            IVsSolution sln = GetService(typeof(SVsSolution)) as IVsSolution;

            IVsHierarchy hierarchy;
            sln.GetProjectOfUniqueName(proj.FullName, out hierarchy);

            return hierarchy as IVsBuildPropertyStorage;
        }

        private const string WebsiteKind = "{E24C65DC-7377-472b-9ABA-BC803B73C61A}";
        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        private string GetCustomProperty(string name, Project proj)
        {
            if (!WebsiteKind.Equals(proj.Kind, StringComparison.Ordinal))
            {
                var storage = GetVsBuildPropertyStorage(proj);
                string value;
                storage.GetPropertyValue(
                    name,
                    string.Empty, (uint)_PersistStorageType.PST_PROJECT_FILE,
                    out value);

                return value;
            }

            return GetWebsiteCustomProperty<string>(name, proj);
        }

        private void SetCustomProperty(string name, string value, Project proj)
        {
            if (!WebsiteKind.Equals(proj.Kind, StringComparison.Ordinal))
            {
                var storage = GetVsBuildPropertyStorage(proj);
                ErrorHandler.ThrowOnFailure(
                    storage.SetPropertyValue(
                        name, string.Empty, (uint)_PersistStorageType.PST_PROJECT_FILE, value));
            }
            else
            {
                SetWebsiteCustomProperty(name, value, proj);
            }
        }

        private static T GetWebsiteCustomProperty<T>(string name, Project proj)
        {
            string cacheDir = VSUtilities.GetWebsiteCacheFolder(proj);
            if (cacheDir != null)
            {
                string filename = string.Concat(cacheDir, name, ".xml");
                if (File.Exists(filename))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(T));
                    using (FileStream fin = File.OpenRead(filename))
                    {
                        return (T)serializer.Deserialize(fin);
                    }
                }
            }

            return default(T);
        }

        private static void SetWebsiteCustomProperty<T>(string name, T value, Project proj)
        {
            string cacheDir = VSUtilities.GetWebsiteCacheFolder(proj);
            if (cacheDir != null)
            {
                string filename = string.Concat(cacheDir, name, ".xml");
                XmlSerializer serializer = new XmlSerializer(typeof(T));
                using (FileStream fout = File.Create(filename))
                {
                    serializer.Serialize(fout, value);
                }
            }
        }


        private List<string> GetSkippedNodes(Project proj)
        {
            List<string> skipped;
            if (SolutionFolderKind.Equals(proj.Kind, StringComparison.Ordinal))
            {
                skipped = new List<string>();
            }
            else
            {
                if (!_skippedNodes.TryGetValue(proj.FullName, out skipped))
                {
                    skipped = new List<string>(8);
                    _skippedNodes.Add(proj.FullName, skipped);

                    string value = GetCustomProperty("JSLintSkip", proj);
                    if (value != null)
                    {
                        skipped.AddRange(value.Split('|'));
                    }
                }
            }

            return skipped;
        }

        private static string GetRelativeName(ProjectItem projItem)
        {
            try
            {

                return GetFileName(projItem).Substring(
                    Path.GetDirectoryName(projItem.ContainingProject.FullName).Length);
            }
            catch
            {
                return "";
            }
        }

        private bool IsNodeSkipped(ProjectItem projItem, out bool skippedByFolder)
        {
            skippedByFolder = false;
            string name = GetRelativeName(projItem);
            foreach (var skipped in GetSkippedNodes(projItem.ContainingProject))
            {
                if (skipped.Length > 0 && name.StartsWith(skipped))
                {
                    if (name.Length > skipped.Length)
                    {
                        skippedByFolder = true;
                    }

                    return true;
                }
            }

            return false;
        }

        private bool IsNodeSkipped(ProjectItem projItem)
        {
            bool skippedByFolder;
            return IsNodeSkipped(projItem, out skippedByFolder);
        }

        private void ToggleSkip(ProjectItem projItem)
        {
            var proj = projItem.ContainingProject;
            var skipped = GetSkippedNodes(proj);
            var name = GetRelativeName(projItem);
            if (skipped.Contains(name))
            {
                skipped.Remove(name);
            }
            else
            {
                skipped.Add(name);
            }

            SetCustomProperty("JSLintSkip", string.Join("|", skipped), proj);
        }

        private void AnalyzeProjectItems(ProjectItems projItems, out bool reachedTreshold)
        {
            var currentOptions = OptionsProviderRegistry.CurrentOptions;
            reachedTreshold = false;
            for (int i = 1; i <= projItems.Count; i++)
            {
                ProjectItem item = projItems.Item(i);
                var filename = GetFileName(item);
                IncludeFileType fileType = GetFileType(filename);

                if (fileType == IncludeFileType.Folder) // folder
                {
                    AnalyzeProjectItems(item.ProjectItems, out reachedTreshold);
                }
                else if ((currentOptions.BuildFileTypes & fileType) > 0 &&
                    item.FileCount == 1 &&
                    !IsNodeSkipped(item))
                {
                    ClearErrors(filename);
                    AnalyzeFile(filename, out reachedTreshold);
                }

                if (reachedTreshold)
                {
                    break;
                }
            }
        }

        private void AnalyzeFile(string filename, out bool reachedThreshold)
        {
            try
            {
                string text = File.ReadAllText(filename);
                Analyze(text, filename);
            }
            catch (Exception e)
            {
                WriteToErrorList(e.Message);
            }
            finally
            {
                reachedThreshold = _errorCount > Threshold;

                if (reachedThreshold)
                {
                    WriteToErrorList(
                        $"Error threshold of {Threshold} reached. JSLint will not generate any more errors for this operation.");
                }
            }
        }

        private void Analyze(string fragment, string filename, int lineOffset = 1, int charOffset = 1)
        {
            if (String.IsNullOrWhiteSpace(fragment))
            {
                return;
            }

            try
            {
                int firstLineOfFragment = 0;
                IncludeFileType fileType = GetFileType(filename, fragment);

                if (fileType == IncludeFileType.CSS)
                {
                    if (!fragment.StartsWith("@charset"))
                    {
                        if (lineOffset > 1 || OptionsProviderRegistry.CurrentOptions.FakeCSSCharset)
                        {
                            fragment = "@charset \"UTF-8\";" + "\n" + fragment;
                            firstLineOfFragment = 1;
                        }
                        else
                        {
                            WriteError(
                                "",
                                lineOffset,
                                1,
                                "CSS Files must begin @charset to be parsed by JS Lint",
                                filename);
                            return;
                        }
                    }
                }

                var ignoreErrorHandler = new IgnoreErrorSectionsHandler(fragment);

                var isJs = fileType == IncludeFileType.JS;
                var errors = _linter.Lint(fragment, OptionsProviderRegistry.CurrentOptions.JSLintOptions, isJs);
                foreach (var error in errors)
                {
                    if (ignoreErrorHandler.IsErrorIgnored(error.Line, error.Column))
                    {
                        continue;
                    }

                    if (++_errorCount > Threshold)
                    {
                        break;
                    }

                    WriteError(
                        error.Evidence,
                        error.Line + lineOffset - (1 + firstLineOfFragment),
                        error.Line == 1 + firstLineOfFragment
                            ? error.Column + charOffset - 1
                            : error.Column,
                        error.Message,
                        filename,
                        !isJs);
                }

                if (OptionsProviderRegistry.CurrentOptions.TODOEnabled && isJs)
                {
                    var todos = TodoFinder.FindTodos(fragment);

                    foreach (var error in todos)
                    {
                        WriteToDo(
                            error.Line + lineOffset - (1 + firstLineOfFragment),
                            error.Line == 1 + firstLineOfFragment
                                ? error.Column + charOffset - 1
                                : error.Column,
                            error.Message,
                            filename);
                    }
                }
            }
            catch (Exception e)
            {
                WriteToErrorList(e.Message);
            }
        }

        private void AnalyzeFragment(string fragment, string filename, int lineOffset = 1, int charOffset = 1)
        {
            ResetErrorCount();

            Analyze(fragment, filename, lineOffset, charOffset);

            UpdateStatusBar(_errorCount > Threshold);
        }

        private IncludeFileType GetFileType(string filename, string fragment = null)
        {
            if (filename.EndsWith(".js", StringComparison.InvariantCultureIgnoreCase))
            {
                return IncludeFileType.JS;
            }

            if (filename.EndsWith(".css", StringComparison.InvariantCultureIgnoreCase))
            {
                return IncludeFileType.CSS;
            }

            if (filename.EndsWith(".htm", StringComparison.InvariantCultureIgnoreCase) ||
                filename.EndsWith(".html", StringComparison.InvariantCultureIgnoreCase) ||
                filename.EndsWith(".aspx", StringComparison.InvariantCultureIgnoreCase) ||
                filename.EndsWith(".ascx", StringComparison.InvariantCultureIgnoreCase))
            {
                if (fragment != null && !fragment.TrimStart().StartsWith("<"))
                {
                    return IncludeFileType.JS;
                }
                else
                {
                    if (filename.EndsWith(".htm", StringComparison.InvariantCultureIgnoreCase) ||
                        filename.EndsWith(".html", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return IncludeFileType.HTML;
                    }
                }
            }

            if (filename.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return IncludeFileType.Folder;
            }

            return IncludeFileType.None;
        }

        private void WriteToErrorList(string message)
        {
            _errorListHelper.Write(
                TaskCategory.BuildCompile,
                TaskErrorCategory.Error,
                message, string.Empty, 0, 0);
        }

        private void WriteToDo(int line, int column, string message, string filename)
        {
            Write(OptionsProviderRegistry.CurrentOptions.TODOCategory, line, column, "TODO", message, filename);
        }


        private void WriteError(string evidence, int line, int column, string message, string filename, bool forceJsLint = false)
        {
            string msgFormat;
            if (forceJsLint)
            {
                msgFormat = "JS Lint (Htm/Css): ";
            }
            else
            {
                switch (OptionsProviderRegistry.CurrentOptions.JSLintOptions.SelectedLinter)
                {
                    case Linters.JSHint:
                        msgFormat = "JS Hint: ";
                        break;
                    default:
                        msgFormat = "JS Lint: ";
                        break;
                    case Linters.JSLintOld:
                        msgFormat = "JS Lint Old: ";
                        break;
                }
            }
            Write(OptionsProviderRegistry.CurrentOptions.ErrorCategory, line, column, evidence, string.Concat(msgFormat, message), filename);
        }

        private void Write(ErrorCategory category,
            int line, int column, string subcategory, string message, string filename)
        {
            if (category.IsTaskError())
            {
                _errorListHelper.Write(
                    TaskCategory.BuildCompile,
                    (TaskErrorCategory)category,
                    message, filename, line, column);
            }
            else
            {
                var taskList = (TaskList)(_dte2.Windows.Item(EnvDTE.Constants.vsWindowKindTaskList)).Object;
                taskList.TaskItems.Add(
                    "JSLint", subcategory ?? string.Empty, message,
                    vsTaskPriority.vsTaskPriorityHigh, null, true,
                    filename, line);
            }
        }

        private void SuspendErrorList()
        {
            if (OptionsProviderRegistry.CurrentOptions.ErrorCategory.IsTaskError())
            {
                _errorListHelper?.SuspendRefresh();
            }
        }

        private void ResumeErrorList(bool focus = true)
        {
            if (OptionsProviderRegistry.CurrentOptions.ErrorCategory.IsTaskError())
            {
                _errorListHelper?.ResumeRefresh(focus);
            }
        }

        private void ClearErrors(string filename)
        {
            if (OptionsProviderRegistry.CurrentOptions.ErrorCategory.IsTaskError())
            {
                _errorListHelper?.ClearDocument(filename);
            }
        }

        private void ResetErrorCount()
        {
            _errorCount = 0;
        }

        private void UpdateStatusBar(bool reachedTreshold)
        {
            _dte2.StatusBar.Text = $"JS Lint: {_errorCount}{(reachedTreshold ? "+" : string.Empty)} errors";
        }

        #endregion 
    }
}
