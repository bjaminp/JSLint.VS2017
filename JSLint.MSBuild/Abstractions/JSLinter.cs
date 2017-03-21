#pragma warning disable 1591

namespace JSLint.MSBuild.Abstractions
{
    using System.Collections.Generic;
    using JSLint.VS2010.LinterBridge;
    using JSLint.VS2010.OptionClasses;
    using SealedLinter = JSLint.VS2010.LinterBridge.JSLinter;

    public class JSLinter : IJSLinter
    {
        private SealedLinter linter;

        public JSLinter()
        {
            this.linter = new SealedLinter();
        }

        public List<JSLintError> Lint(string javascript, bool isJavaScript)
        {
            return this.linter.Lint(javascript, isJavaScript);
        }

        public List<JSLintError> Lint(string javascript, JSLintOptions configuration, bool isJavaScript)
        {
            return this.linter.Lint(javascript, configuration, isJavaScript);
        }

        public void Dispose()
        {
            this.linter.Dispose();
        }
    }
}
