#pragma warning disable 1591

namespace JSLint.MSBuild.Abstractions
{
    using JSLint.VS2010.OptionClasses;
    using JSLint.VS2010.OptionClasses.OptionProviders;

    public class JSLintFactory : IJSLintFactory
    {
        public IIgnoreErrorSectionsHandler CreateIgnoreErrorSectionsHandler(string contents, Options options)
        {
            return new IgnoreErrorSectionsHandler(contents, options);
        }

        public IOptionsProvider CreateOptionsProvider(string filePath)
        {
            return new FileOptionsProvider("FileOptionsProvider", filePath);
        }

        public IJSLinter CreateLinter()
        {
            return new JSLinter();
        }
    }
}
