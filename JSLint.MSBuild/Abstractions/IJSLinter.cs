#pragma warning disable 1591

namespace JSLint.MSBuild.Abstractions
{
    using System;
    using System.Collections.Generic;
    using JSLint.VS2010.LinterBridge;
    using JSLint.VS2010.OptionClasses;

    public interface IJSLinter : IDisposable
    {
        List<JSLintError> Lint(string javascript, bool isJavaScript);

        List<JSLintError> Lint(string javascript, JSLintOptions configuration, bool isJavaScript);
    }
}
