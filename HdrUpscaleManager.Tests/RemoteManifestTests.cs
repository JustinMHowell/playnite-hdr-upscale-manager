using System.Collections.Generic;
using HdrUpscaleManager.Models;
using Newtonsoft.Json;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class RemoteManifestTests
    {
        [Fact]
        public void Deserialize_IgnoresUnknownFields()
        {
            var json = @"{
                ""version"": 1,
                ""somethingRhiHasThatWeDontModelYet"": { ""nested"": true },
                ""nativeHdrGames"": [""Returnal"", ""Lies of P""]
            }";

            var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);

            Assert.Equal(1, manifest.Version);
            Assert.Equal(new[] { "Returnal", "Lies of P" }, manifest.NativeHdrGames);
        }

        [Fact]
        public void Deserialize_MissingFields_DefaultToNullNotThrow()
        {
            var json = @"{ ""version"": 2 }";

            var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);

            Assert.Equal(2, manifest.Version);
            Assert.Null(manifest.Blacklist);
            Assert.Null(manifest.WikiNameOverrides);
            Assert.Null(manifest.AddonPacks);
        }

        [Fact]
        public void Normalize_NullManifest_NoOp()
        {
            RemoteManifest.Normalize(null);
        }

        [Fact]
        public void Normalize_LeavesNullDictionariesNull()
        {
            var manifest = new RemoteManifest();

            RemoteManifest.Normalize(manifest);

            Assert.Null(manifest.WikiNameOverrides);
            Assert.Null(manifest.GameNotes);
        }

        [Fact]
        public void Normalize_MakesNameLookupsCaseInsensitive()
        {
            var manifest = new RemoteManifest
            {
                WikiNameOverrides = new Dictionary<string, string>
                {
                    ["Cyberpunk 2077"] = "Cyberpunk2077"
                }
            };

            RemoteManifest.Normalize(manifest);

            // Playnite's Game.Name casing won't always match the manifest's casing exactly —
            // this is the actual risk this test is guarding against (see PLAN.md §7).
            Assert.True(manifest.WikiNameOverrides.TryGetValue("cyberpunk 2077", out var value));
            Assert.Equal("Cyberpunk2077", value);
            Assert.True(manifest.WikiNameOverrides.ContainsKey("CYBERPUNK 2077"));
        }
    }
}
