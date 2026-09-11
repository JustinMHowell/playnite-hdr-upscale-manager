using System;
using System.Reflection;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class CompressionAssemblyResolverTests
    {
        private static AssemblyName Buffers(string version, string token = "cc7b13ffcd2ddd51", string culture = "neutral") =>
            new AssemblyName($"System.Buffers, Version={version}, Culture={culture}, PublicKeyToken={token}");

        [Theory]
        [InlineData("4.0.2.0", "4.0.5.0", true)]
        [InlineData("4.0.5.0", "4.0.5.0", true)]
        [InlineData("4.0.6.0", "4.0.5.0", false)]
        [InlineData("4.0.2.0", "5.0.0.0", false)]
        public void IsCompatible_OnlySameMajorWithoutDowngrade(string requested, string candidate, bool expected)
        {
            Assert.Equal(expected, CompressionAssemblyResolver.IsCompatible(Buffers(requested), Buffers(candidate)));
        }

        [Fact]
        public void IsCompatible_RejectsDifferentIdentity()
        {
            var requested = Buffers("4.0.2.0");
            Assert.False(CompressionAssemblyResolver.IsCompatible(requested, Buffers("4.0.5.0", "b03f5f7f11d50a3a")));
            Assert.False(CompressionAssemblyResolver.IsCompatible(requested, Buffers("4.0.5.0", culture: "fr")));
            Assert.False(CompressionAssemblyResolver.IsCompatible(Buffers("4.0.2.0", "null"), Buffers("4.0.5.0", "null")));
            Assert.False(CompressionAssemblyResolver.IsCompatible(requested,
                new AssemblyName("System.Memory, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51")));
            var unrelated = new AssemblyName("Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed");
            Assert.False(CompressionAssemblyResolver.IsCompatible(unrelated, unrelated));
        }

        [Fact]
        public void Resolve_IgnoresMissingOrUnrelatedRequester()
        {
            var name = Buffers("4.0.2.0").FullName;
            Assert.Null(CompressionAssemblyResolver.Resolve(null, new ResolveEventArgs(name)));
            Assert.Null(CompressionAssemblyResolver.Resolve(null, new ResolveEventArgs(name, typeof(object).Assembly)));
            Assert.Null(CompressionAssemblyResolver.Resolve(null, new ResolveEventArgs(name, GetType().Assembly)));
        }

        [Fact]
        public void Resolve_IgnoresUnknownDependencyEvenForPlugin()
        {
            Assert.Null(CompressionAssemblyResolver.Resolve(null,
                new ResolveEventArgs("Unknown.Dependency, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                    typeof(SevenZipExtractor).Assembly)));
        }
    }
}