using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HdrUpscaleManager.Services.Addons
{
    /* Playnite owns the AppDomain/config, so a plugin DLL's binding redirects aren't
       applied. SharpCompress's netstandard build references older assembly versions
       than its NuGet dependencies ship. Resolve only that private dependency chain,
       never arbitrary System.* requests from Playnite or another extension. */
    internal static class CompressionAssemblyResolver
    {
        private static readonly object RegistrationLock = new object();
        private static bool registered;
        [ThreadStatic]
        private static int extractionDepth;
        private static readonly string PluginDirectory = Path.GetDirectoryName(typeof(CompressionAssemblyResolver).Assembly.Location);
        private static readonly HashSet<string> DependencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Buffers",
            "System.Memory",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Threading.Tasks.Extensions",
            "System.Numerics.Vectors",
            "System.Text.Encoding.CodePages",
            "Microsoft.Bcl.AsyncInterfaces"
        };

        private static void Register()
        {
            lock (RegistrationLock)
            {
                if (registered) return;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                registered = true;
            }
        }

        internal static IDisposable BeginExtraction()
        {
            Register();
            return new ExtractionScope();
        }

        private sealed class ExtractionScope : IDisposable
        {
            private bool disposed;
            internal ExtractionScope() => extractionDepth++;
            public void Dispose()
            {
                if (disposed) return;
                extractionDepth--;
                disposed = true;
            }
        }

        internal static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var requester = args.RequestingAssembly;
            // The CLR sometimes omits RequestingAssembly (including SharpCompress's
            // buffer initialization). Accept those requests only on the thread currently
            // executing our synchronous extraction, not process-wide for its lifetime.
            if (requester == null && extractionDepth == 0) return null;
            if (requester != null && requester.IsDynamic) return null;

            try
            {
                if (requester != null)
                {
                    var requesterName = requester.GetName().Name;
                    if (requester != typeof(CompressionAssemblyResolver).Assembly &&
                        !string.Equals(requesterName, "SharpCompress", StringComparison.OrdinalIgnoreCase) &&
                        !DependencyNames.Contains(requesterName)) return null;

                    // Matching the name alone could hijack another plugin's SharpCompress.
                    if (!string.Equals(Path.GetDirectoryName(requester.Location), PluginDirectory,
                        StringComparison.OrdinalIgnoreCase)) return null;
                }

                var requested = new AssemblyName(args.Name);
                if (!DependencyNames.Contains(requested.Name)) return null;

                var path = Path.Combine(PluginDirectory, requested.Name + ".dll");
                if (!File.Exists(path)) return null;
                var candidate = AssemblyName.GetAssemblyName(path);
                if (!IsCompatible(requested, candidate)) return null;

                // Load by absolute path, not Assembly.Load(args.Name), which would
                // recursively raise AssemblyResolve for the same failed identity.
                return Assembly.LoadFrom(path);
            }
            catch (Exception ex) when (ex is IOException || ex is BadImageFormatException ||
                ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Log.Warn(ex, $"Could not resolve bundled compression dependency '{args.Name}'.");
                return null;
            }
        }

        internal static bool IsCompatible(AssemblyName requested, AssemblyName candidate)
        {
            var requestedToken = requested.GetPublicKeyToken();
            var candidateToken = candidate.GetPublicKeyToken();
            return DependencyNames.Contains(requested.Name) &&
                string.Equals(requested.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(requested.CultureName ?? "", candidate.CultureName ?? "", StringComparison.OrdinalIgnoreCase) &&
                requestedToken != null && requestedToken.Length > 0 && candidateToken != null &&
                requestedToken.SequenceEqual(candidateToken) &&
                requested.Version != null && candidate.Version != null &&
                requested.Version.Major == candidate.Version.Major &&
                candidate.Version >= requested.Version;
        }
    }
}