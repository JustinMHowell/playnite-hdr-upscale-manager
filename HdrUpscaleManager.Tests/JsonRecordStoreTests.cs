using System;
using System.IO;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class JsonRecordStoreTests : IDisposable
    {
        private class SampleRecord
        {
            public string Name { get; set; }
        }

        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Fact]
        public void Get_NoRecordSaved_ReturnsNull()
        {
            var store = new JsonRecordStore<SampleRecord>(directory, "sample.json");

            Assert.Null(store.Get("game-1"));
        }

        [Fact]
        public void Set_ThenGet_RoundTripsRecord()
        {
            var store = new JsonRecordStore<SampleRecord>(directory, "sample.json");

            store.Set("game-1", new SampleRecord { Name = "Code Vein" });
            var loaded = store.Get("game-1");

            Assert.NotNull(loaded);
            Assert.Equal("Code Vein", loaded.Name);
        }

        [Fact]
        public void Remove_DeletesRecord()
        {
            var store = new JsonRecordStore<SampleRecord>(directory, "sample.json");
            store.Set("game-1", new SampleRecord { Name = "Code Vein" });

            store.Remove("game-1");

            Assert.Null(store.Get("game-1"));
        }

        [Fact]
        public void Set_PreservesOtherKeysRecords()
        {
            var store = new JsonRecordStore<SampleRecord>(directory, "sample.json");
            store.Set("game-1", new SampleRecord { Name = "Code Vein" });
            store.Set("game-2", new SampleRecord { Name = "Lies of P" });

            Assert.NotNull(store.Get("game-1"));
            Assert.NotNull(store.Get("game-2"));
        }

        [Fact]
        public void DifferentFileNames_DoNotCollideInSameDirectory()
        {
            var storeA = new JsonRecordStore<SampleRecord>(directory, "a.json");
            var storeB = new JsonRecordStore<SampleRecord>(directory, "b.json");
            storeA.Set("game-1", new SampleRecord { Name = "From A" });

            Assert.Null(storeB.Get("game-1"));
        }
    }
}
