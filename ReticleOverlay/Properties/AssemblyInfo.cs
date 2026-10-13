// Opts the assembly into the source-generated P/Invoke marshalling model required by [LibraryImport].
// Without this, [LibraryImport] cannot handle non-blittable parameter types such as
// System.Drawing.Point (MonitorFromPoint) and char (VkKeyScanW).
// See: https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib1051
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
