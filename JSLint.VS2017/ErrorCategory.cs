using JSLint.Framework;

namespace JSLint.VS2017
{
    internal static class ErrorCategoryExtensions
    {
        internal static bool IsTaskError(this ErrorCategory cat)
        {
            return System.Enum.IsDefined(
                typeof(Microsoft.VisualStudio.Shell.TaskErrorCategory),
                (int)cat);
        }
    }
}

