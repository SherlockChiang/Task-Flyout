using System;

namespace Task_Flyout.Services
{
    internal static class ImapMovePolicy
    {
        public static bool SupportsSafeMove(bool nativeMove, bool uidPlus)
            => nativeMove && uidPlus;

        public static bool IsSelectableDestination(string fullName, string currentFolder, bool noSelect, bool nonExistent)
            => !string.IsNullOrWhiteSpace(fullName) &&
               !string.Equals(fullName, currentFolder, StringComparison.Ordinal) &&
               !noSelect && !nonExistent;

        public static bool HasAuthoritativeIdentity(uint uid, uint uidValidity)
            => uid > 0 && uidValidity > 0;
    }
}
