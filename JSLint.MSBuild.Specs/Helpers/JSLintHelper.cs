﻿namespace JSLint.MSBuild.Specs.Helpers
{
    using JSLint.VS2010.LinterBridge;

    public static class JSLintHelper
    {
        public static JSLintError CreateJSLintError(int line, int column, string message, string evidence)
        {
            var lintError = new JSLintError();

            ReflectionHelper.SetPropertyValue(lintError, "Line", line);
            ReflectionHelper.SetPropertyValue(lintError, "Column", column);
            ReflectionHelper.SetPropertyValue(lintError, "Message", message);
            ReflectionHelper.SetPropertyValue(lintError, "Evidence", evidence);

            return lintError;
        }
    }
}
