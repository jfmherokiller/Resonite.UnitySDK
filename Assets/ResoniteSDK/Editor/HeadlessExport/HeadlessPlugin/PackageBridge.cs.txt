using System;
using System.IO;
using System.Threading.Tasks;
using Elements.Core;
using Elements.Data;
using FrooxEngine;

[assembly: DataModelAssembly(DataModelAssemblyType.Core)]

namespace ResoniteUnitySDK.Headless
{
    /// <summary>Lets ResoniteLink drive the native package exporter/importer in a headless world.</summary>
    [Category(new[] { "Resonite Unity SDK" })]
    public class PackageBridge : Component
    {
        /// <summary>Exports root exactly like the dash Export dialog's Resonite Package option.</summary>
        [SyncMethod(typeof(Func<Slot, string, string, bool, Task>), new string[] { })]
        public static async Task ExportPackage(Slot root, string folder, string name, bool includeVariants)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            Directory.CreateDirectory(folder);
            var holder = root.World.RootSlot.AddSlot("Package Export", persistent: false);
            try
            {
                var exportable = holder.AttachComponent<PackageExportable>();
                exportable.Root.Target = root;
                if (!await exportable.Export(folder, name, includeVariants ? 1 : 0))
                    throw new InvalidOperationException("PackageExportable.Export returned false");
            }
            finally
            {
                await default(ToWorld);
                holder.Destroy();
            }
        }

        /// <summary>Imports a package under parent, for round-trip checks.</summary>
        [SyncMethod(typeof(Func<string, Slot, Task>), new string[] { })]
        public static async Task ImportPackage(string file, Slot parent)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException(file);
            await PackageImporter.ImportPackage(file, parent);
        }
    }
}
