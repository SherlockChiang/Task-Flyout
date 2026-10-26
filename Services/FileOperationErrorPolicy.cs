using System;
using System.IO;
using System.Xml;

namespace Task_Flyout.Services;

internal readonly record struct FileOperationError(string ResourceKey, string Fallback);

// File operations report an actionable, localizable category, not runtime exception text.
internal static class FileOperationErrorPolicy
{
    internal static FileOperationError Describe(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => new("FileError_NotFound", "The file is no longer available. Select it again."),
        UnauthorizedAccessException => new("FileError_AccessDenied", "Access to the file was denied. Choose a location you can access."),
        InvalidDataException or XmlException => new("FileError_InvalidData", "The file format is invalid or unsupported. Check the file and try again."),
        IOException => new("FileError_IO", "The file could not be read or written. Check available space and whether another app is using it."),
        OperationCanceledException => new("FileError_Canceled", "The file operation was canceled. Try again when ready."),
        _ => new("FileError_Unexpected", "The file operation could not finish. Select the file or destination again and retry.")
    };
}
