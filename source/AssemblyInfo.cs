using System.Reflection;

[assembly: AssemblyVersion("1.0.0")]

#if !MIRROR_35_0_OR_NEWER
#error Only supports mirror version v35 or later
#endif
#if MIRROR_36_0_OR_NEWER
#warning Latest supported version is v35
#endif
