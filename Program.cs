using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using CmdLine;
using ThomasJepp.SaintsRow;
using ThomasJepp.SaintsRow.AssetAssembler;
using ThomasJepp.SaintsRow.Bitmaps.Version13;
using ThomasJepp.SaintsRow.ClothSimulation.Version02;
using ThomasJepp.SaintsRow.GameInstances;
using ThomasJepp.SaintsRow.Localization;
using ThomasJepp.SaintsRow.Packfiles;
using ThomasJepp.SaintsRow.Meshes.StaticMesh;
using ThomasJepp.SaintsRow.Strings;
using ThomasJepp.SaintsRow.VFile;

namespace Flow.WeaponClone
{
    class Program
    {
        [CommandLineArguments(Program = "Flow.WeaponClone", Title = "Saints Row IV Weapon clone tool", Description = "Universal tool for cloning weapon costumes and skins.")]
        internal class Options
        {
            [CommandLineParameter(Name = "source", ParameterIndex = 1, Required = true, Description = "The name of the original costume/skin to clone.")]
            public string Source { get; set; }

            [CommandLineParameter(Name = "name", ParameterIndex = 2, Required = true, Description = "The new name to give the cloned weapon/costume/skin.")]
            public string NewName { get; set; }

            [CommandLineParameter(Name = "function", ParameterIndex = 3, Required = true, Description = "either \"weapon\", \"costume\" or \"skin\"\n\"weapon\" - clones a COSTUME to a new WEAPON.\n\"costume\" - clones a COSTUME to a new COSTUME for the same weapon.\n\"skin\" - clones a SKIN to a new SKIN.\n")]
            public string cloneFunction { get; set; }

            [CommandLineParameter(Name = "output", ParameterIndex = 4, Required = false, Description = "The directory to output the new weapon/costume/skin to. Defaults to the local directory if not specified.")]
            public string Output { get; set; }
        }

        static IContainer FindContainer(IGameInstance sriv, string containerName)
        {
            string[] asmNames = new string[]
            {
                "items_preload_containers.asm_pc",
                "items_containers.asm_pc",
                "main_streaming_weapons.asm_pc",
                "dlc1_items_containers.asm_pc",
                "dlc2_items_containers.asm_pc",
                "dlc3_items_containers.asm_pc",
                "dlc4_items_containers.asm_pc",
                "dlc5_items_containers.asm_pc",
                "dlc6_items_containers.asm_pc"
            };

            string name = Path.GetFileNameWithoutExtension(containerName);

            foreach (string asmName in asmNames)
            {
                using (Stream s = sriv.OpenPackfileFile(asmName))
                {
                    IAssetAssemblerFile asm = AssetAssemblerFile.FromStream(s);

                    foreach (var container in asm.Containers)
                    {
                        if (container.Name == name)
                            return container;
                    }
                }
            }

            return null;
        }

        static IPrimitive FindPrimitive(IContainer container, string primitiveName)
        {
            string primitiveMatchName = primitiveName.ToLowerInvariant();

            foreach (IPrimitive primitive in container.Primitives)
            {
                if (primitive.Name.ToLowerInvariant() == primitiveMatchName)
                {
                    return primitive;
                }
            }

            return null;
        }

        static Stream ProcessClothSim(IPackfileEntry oldFile, string newName)
        {
            MemoryStream outStream = new MemoryStream();
            using (Stream inStream = oldFile.GetStream())
            {
                ClothSimulationFile file = new ClothSimulationFile(inStream);
                file.Header.Name = newName;
                file.Save(outStream);
            }

            return outStream;
        }

        static IPackfileEntry FindPackfileEntry(IPackfile packfile, string extension)
        {
            foreach (IPackfileEntry entry in packfile.Files)
            {
                string entryExtension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (entryExtension == extension)
                    return entry;
            }

            return null;
        }

        static Dictionary<string, string> textureNameMap = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

        static bool ClonePackfile(IGameInstance sriv, string packfileName, IAssetAssemblerFile newAsm, string newName, string newStr2Filename)
        {
            using (Stream oldStream = sriv.OpenPackfileFile(packfileName))
            {
                if (oldStream == null)
                    return false;

                IContainer oldContainer = FindContainer(sriv, packfileName);
                if (oldContainer == null)
                    return false;

                using (IPackfile oldPackfile = Packfile.FromStream(oldStream, true))
                {
                    using (IPackfile newPackfile = Packfile.FromVersion(0x0A, true))
                    {
                        newPackfile.IsCompressed = true;
                        newPackfile.IsCondensed = true;

                        foreach (var file in oldPackfile.Files)
                        {
                            string extension = Path.GetExtension(file.Name);
                            IPrimitive primitive = FindPrimitive(oldContainer, file.Name);

                            switch (extension)
                            {
                                case ".sim_pc":
                                    {
                                        string newFilename = newName + extension;
                                        Stream newStream = ProcessClothSim(file, newName);
                                        newPackfile.AddFile(newStream, newFilename);
                                        primitive.Name = newFilename;
                                        break;
                                    }

                                case ".ccmesh_pc":
                                case ".gcmesh_pc":
                                case ".cpeg_pc":
                                case ".gpeg_pc":
                                case ".csmesh_pc":
                                case ".gsmesh_pc":
                                case ".matlib_pc":
                                case ".rig_pc":
                                    {
                                        string newFilename = newName + extension;
                                        newPackfile.AddFile(file.GetStream(), newFilename);
                                        if (primitive != null)
                                            primitive.Name = newFilename;
                                        break;
                                    }

                                case ".cvbm_pc": //don't add texture files for weapon icons to packfiles/containers. Include a loose always_loaded peg in the mod instead
                                case ".gvbm_pc": //For now, if it's not a ui name, add it without changing the name. Eventually convert vbm to peg and rename.
                                    {
                                        if (!Path.GetFileNameWithoutExtension(file.Name).StartsWith("ui_hud"))
                                            newPackfile.AddFile(file.GetStream(), file.Name);
                                        else
                                            oldContainer.Primitives.Remove(primitive);

                                        break;
                                    }

                                //keep name
                                case ".cefct_pc":
                                case ".gefct_pc":
                                    {
                                        newPackfile.AddFile(file.GetStream(), file.Name);
                                        break;
                                    }

                                default:
                                    throw new Exception(String.Format("Unrecognised extension: {0}", extension));
                            }
                        }

                        oldContainer.Name = Path.GetFileNameWithoutExtension(newStr2Filename);
                        newAsm.Containers.Add(oldContainer);

                        using (Stream s = File.Create(newStr2Filename))
                        {
                            newPackfile.Save(s);
                        }

                        newPackfile.Update(oldContainer);
                    }
                }
            }

            return true;
        }

        static XElement FindCostumeSkin(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "weapon_skins.xtbl",
                "dlc1_weapon_skins.xtbl",
                "dlc2_weapon_skins.xtbl",
                "dlc3_weapon_skins.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Skin"))
                    {
                        if (node.Element("Name") != null)
                        {
                            string itemName = node.Element("Name").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                return node;
                            }
                        }
                    }
                }
            }

            return null;
        }

        static List<XElement> FindCostumeSkins(string costumeName, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "weapon_skins.xtbl",
                "dlc1_weapon_skins.xtbl",
                "dlc2_weapon_skins.xtbl",
                "dlc3_weapon_skins.xtbl"
            };

            List<XElement> nodes = new List<XElement>();
            string targetName = costumeName.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Skin"))
                    {
                        if (node.Element("Costume") != null)
                        {
                            string itemName = node.Element("Costume").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                nodes.Add(node);
                            }
                        }
                    }
                }
            }

            if (!(nodes.Count > 0))
                return null;
            return nodes;
        }

        static List<XElement> FindWeaponUpgrades(string w_info, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "weapon_upgrades.xtbl",
                "dlc1_weapon_upgrades.xtbl",
                "dlc2_weapon_upgrades.xtbl",
                "dlc3_weapon_upgrades.xtbl",
                "dlc4_weapon_upgrades.xtbl",
                "dlc5_weapon_upgrades.xtbl",
                "dlc6_weapon_upgrades.xtbl"
            };

            List<XElement> nodes = new List<XElement>();
            string targetName = w_info.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Weapon_Upgrade"))
                    {
                        if (node.Element("W_Info") != null)
                        {
                            string itemName = node.Element("W_Info").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                nodes.Add(node);
                            }
                        }
                    }
                }
            }

            if (!(nodes.Count > 0))
                return null;
            return nodes;
        }

        static XElement FindStoreWeapon(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "store_weapons.xtbl",
                "dlc1_store_weapons.xtbl",
                "dlc2_store_weapons.xtbl",
                "dlc3_store_weapons.xtbl",
                "dlc4_store_weapons.xtbl",
                "dlc5_store_weapons.xtbl",
                "dlc6_store_weapons.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");
                    var store = table.Descendants("Store_Weapons");
                    var weapons = store.Descendants("Weapons_List");

                    foreach (var node in weapons.Descendants("Entry"))
                    {
                        if (node.Element("Weapon") != null)
                        {
                            string itemName = node.Element("Weapon").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                return node;
                            }
                        }
                    }
                }
            }

            return null;
        }

        static XElement FindInventoryItem(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "items_inventory.xtbl",
                "dlc1_items_inventory.xtbl",
                "dlc2_items_inventory.xtbl",
                "dlc3_items_inventory.xtbl",
                "dlc4_items_inventory.xtbl",
                "dlc5_items_inventory.xtbl",
                "dlc6_items_inventory.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Inventory_Item"))
                    {

                        if (node.Element("Name") != null)
                        {
                            string itemName = node.Element("Name").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                return node;
                            }
                        }
                    }
                }
            }

            return null;
        }

        static XElement FindWeapon(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "weapons.xtbl",
                "dlc1_weapons.xtbl",
                "dlc2_weapons.xtbl",
                "dlc3_weapons.xtbl",
                "dlc4_weapons.xtbl",
                "dlc5_weapons.xtbl",
                "dlc6_weapons.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Element("root").Descendants("Table");

                    foreach (var node in table.Descendants("Weapon"))
                    {
                        if (node.Element("Name") != null)
                        {
                            string itemName = node.Element("Name").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                return node;
                            }
                        }
                    }
                }
            }

            return null;
        }

        static XElement FindItem3d(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "items_3d.xtbl",
                "dlc1_items_3d.xtbl",
                "dlc2_items_3d.xtbl",
                "dlc3_items_3d.xtbl",
                "dlc4_items_3d.xtbl",
                "dlc5_items_3d.xtbl",
                "dlc6_items_3d.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Item"))
                    {
                        if (node.Element("Name") != null)
                        {
                            string itemName = node.Element("Name").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                return node;
                            }
                        }
                    }
                }
            }

            return null;
        }

        static XElement FindWeaponCostume(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "weapon_costumes.xtbl",
                "dlc1_weapon_costumes.xtbl",
                "dlc2_weapon_costumes.xtbl",
                "dlc3_weapon_costumes.xtbl",
                "dlc4_weapon_costumes.xtbl",
                "dlc5_weapon_costumes.xtbl",
                "dlc6_weapon_costumes.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Costume"))
                    {
                        if (node.Element("Name") != null)
                        {
                            string itemName = node.Element("Name").Value.ToLowerInvariant();
                            if (itemName == targetName)
                            {
                                return node;
                            }
                        }
                    }
                }
            }

            return null;
        }

        static XElement FindCustomizationItem(string name, IGameInstance sriv)
        {
            string[] xtblNames = new string[]
            {
                "customization_items.xtbl",
                "dlc1_customization_items.xtbl",
                "dlc2_customization_items.xtbl",
                "dlc3_customization_items.xtbl",
                "dlc4_customization_items.xtbl",
                "dlc5_customization_items.xtbl",
                "dlc6_customization_items.xtbl"
            };

            string targetName = name.ToLowerInvariant();

            foreach (string xtblName in xtblNames)
            {
                using (Stream s = sriv.OpenPackfileFile(xtblName))
                {
                    XDocument xml = XDocument.Load(s);

                    var table = xml.Descendants("Table");

                    foreach (var node in table.Descendants("Customization_Item"))
                    {
                        string itemName = node.Element("Name").Value.ToLowerInvariant();

                        if (itemName == targetName)
                        {
                            return node;
                        }
                    }
                }
            }

            return null;
        }

        static Dictionary<Language, Dictionary<uint, string>> languageStrings = new Dictionary<Language, Dictionary<uint, string>>();

        static void LoadStrings(IGameInstance sriv)
        {
            var results = sriv.SearchForFiles("*.le_strings");
            foreach (var result in results)
            {
                string filename = result.Value.Filename.ToLowerInvariant();
                filename = Path.GetFileNameWithoutExtension(filename);

                string[] pieces = filename.Split('_');
                string languageCode = pieces.Last();

                Language language = LanguageUtility.GetLanguageFromCode(languageCode);

                if (!languageStrings.ContainsKey(language))
                    languageStrings.Add(language, new Dictionary<uint, string>());

                Dictionary<uint, string> strings = languageStrings[language];

                using (Stream s = sriv.OpenPackfileFile(result.Value.Filename, result.Value.Packfile))
                {
                    StringFile file = new StringFile(s, language, sriv);

                    foreach (var hash in file.GetHashes())
                    {
                        if (strings.ContainsKey(hash))
                        {
                            continue;
                        }

                        strings.Add(hash, file.GetString(hash));
                    }
                }
            }
        }

        private static string ReplaceCaseInsensitive(string input, string search, string replacement)
        {
            return Regex.Replace(input, Regex.Escape(search), replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);
        }

        static void costumeToWeaponProgram(Options options)
        {

            if (options.Output == null)
            {
                options.Output = options.NewName;
            }

            string outputFolder = options.Output;

            Directory.CreateDirectory(outputFolder);

            IGameInstance sriv = GameInstance.GetFromSteamId(GameSteamID.SaintsRowIV);
            LoadStrings(sriv);

            IAssetAssemblerFile newAsm_items; //items_containers.asm_pc
            IAssetAssemblerFile newAsm_preload; //items_containers.asm_pc
            IAssetAssemblerFile newAsm_costumes; //mods_costumes.asm_pc
            IAssetAssemblerFile newAsm_skins; //mods_skins.asm_pc

            //Make instances of template_items_containers.asm_pc, add containers in the middle, write them out to files at the end of the algorithm
            using (Stream newAsm_items_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_items = AssetAssemblerFile.FromStream(newAsm_items_Stream);
            }
            using (Stream newAsm_preload_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_preload = AssetAssemblerFile.FromStream(newAsm_preload_Stream);
            }
            using (Stream newAsm_costumes_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_costumes = AssetAssemblerFile.FromStream(newAsm_costumes_Stream);
            }
            using (Stream newAsm_skins_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_skins = AssetAssemblerFile.FromStream(newAsm_skins_Stream);
            }


            XDocument items_3d = null;
            XDocument items_inventory = null;
            XDocument store_weapons = null;
            XDocument weapon_costumes = null;
            XDocument weapon_skins = null;
            XDocument weapon_upgrades = null;
            XDocument weapons = null;

            //Make instances of template_table.xtbl, add entries in the middle, write them out to files at the end of the algorithm
            using (Stream xtblTemplateStream = File.OpenRead(Path.Combine("templates", "template_table.xtbl")))
            {
                items_3d = XDocument.Load(xtblTemplateStream);
                items_inventory = new XDocument(items_3d);
                store_weapons = new XDocument(items_3d);
                weapon_costumes = new XDocument(items_3d);
                weapon_skins = new XDocument(items_3d);
                weapon_upgrades = new XDocument(items_3d);
                weapons = new XDocument(items_3d);
            }

            XElement store_weapons_xmlTree = new XElement("Store_Weapons", new XElement("Name", "Weapons list"),
            new XElement("Weapons_List", null));
            store_weapons.Descendants("Table").First().Add(store_weapons_xmlTree);

            var items_3d_Table = items_3d.Descendants("Table").First();
            var items_inventory_Table = items_inventory.Descendants("Table").First();
            var weapon_costumes_Table = weapon_costumes.Descendants("Table").First();
            var weapon_skins_Table = weapon_skins.Descendants("Table").First();
            var weapon_upgrades_Table = weapon_upgrades.Descendants("Table").First();
            var weapons_Table = weapons.Descendants("Table").First();
            var store_weapons_WeaponsList = store_weapons.Descendants("Table").Descendants("Store_Weapons").Descendants("Weapons_List").First();

            //So far we have empty objects for all necessary xtbl files, ready to be filled with actual data
            //This is where shit gets serious
            XElement costumeNode = FindWeaponCostume(options.Source, sriv);
            string costumeEntry = "";
            if (costumeNode != null)
            {
                costumeEntry = costumeNode.Element("Name").Value;
            }
            else
            {
                Console.WriteLine(String.Format("Couldn't find weapon costume \"{0}\"", options.Source));
                return;
            }
            string weaponEntry = costumeNode.Element("Weapon_Entry").Value; Console.WriteLine("Weapon: " + weaponEntry);
            string item3dEntry = costumeNode.Element("Item_Entry").Value; Console.WriteLine("3D Item: " + item3dEntry);
            string inventoryEntry = costumeNode.Element("Inventory_Entry").Value; Console.WriteLine("Inventory Item: " + inventoryEntry);

            XElement inventoryItemNode = FindInventoryItem(inventoryEntry, sriv);
            XElement item3dNode = FindItem3d(item3dEntry, sriv);
            XElement weaponNode = FindWeapon(weaponEntry, sriv);
            XElement storeWeaponNode = FindStoreWeapon("Pistol-Police", sriv);
            List<XElement> skinNodes = FindCostumeSkins(options.Source, sriv);
            List<XElement> weaponUpgradeNodes = FindWeaponUpgrades(weaponEntry, sriv);

            //CHECK IF ALL NODES WERE FOUND
            if (costumeNode == null)
            {
                Console.WriteLine("Couldn't find costume \"{0}\".", options.Source);
                return;
            }
            else if (inventoryItemNode == null)
            {
                Console.WriteLine("Couldn't find inventory item \"{0}\". Cloning inventory item \"Pistol-Gang\"", inventoryEntry);
                inventoryItemNode = FindInventoryItem("Pistol-Gang", sriv);
            }
            else if (item3dNode == null)
            {
                Console.WriteLine("Couldn't find item \"{0}\".", item3dEntry);
                return;
            }
            else if (weaponNode == null)
            {
                Console.WriteLine("Couldn't find weapon \"{0}\".", weaponEntry);
                return;
            }

            Dictionary<int, XElement> allItemNodes = new Dictionary<int, XElement>();
            allItemNodes.Add(0, item3dNode);

            //GET ALL PROP ITEMS
            int v = 1;
            foreach (XElement propElement in item3dNode.Descendants("Props").Descendants("Prop"))
            {
                string propItemEntry = "";
                if (propElement.Element("Name") != null)
                {
                    propItemEntry = propElement.Element("Name").Value;
                    Console.WriteLine(String.Format("Found prop: {0}", propItemEntry));
                }
                XElement propNode = FindItem3d(propItemEntry, sriv);
                if (propNode != null)
                {
                    allItemNodes.Add(v, propNode);
                }
                v++;

            }

            //SOME STRING STUFF IN ADVANCE
            if (costumeNode.Element("Display_Name") == null)
            {
                costumeNode.Add(new XElement("Display_Name", "CUST_WPN_COSTUME_DESC_LTPISTOL_0"));
            }

            if (inventoryItemNode.Element("DisplayName") == null)
            {
                inventoryItemNode.Add(new XElement("Display_Name", "CUST_WPN_COSTUME_DESC_LTPISTOL_0"));
            }

            if (costumeNode.Element("Description") == null)
            {
                costumeNode.Add(new XElement("Description", "CUST_WPN_COSTUME_DESC_LTPISTOL_0_DESC"));
            }

            if (inventoryItemNode.Element("Description") == null)
            {
                inventoryItemNode.Add(new XElement("Description", "CUST_WPN_COSTUME_DESC_LTPISTOL_0_DESC"));
            }

            string originalDisplayName = costumeNode.Element("Display_Name").Value;
            string originalDescription = costumeNode.Element("Description").Value;
            string originalDisplayName_inv = inventoryItemNode.Element("DisplayName").Value;
            string originalDescription_inv = inventoryItemNode.Element("Description").Value;
            uint originalDisplayStringCrc = Hashes.CrcVolition(originalDisplayName);
            uint originalDescriptionStringCrc = Hashes.CrcVolition(originalDescription);
            uint originalDisplayString_inv_Crc = Hashes.CrcVolition(originalDisplayName_inv);
            uint originalDescriptionString_inv_Crc = Hashes.CrcVolition(originalDescription_inv);
            string newDisplayName = "MOD_WPN_" + options.NewName.ToUpperInvariant();
            string newDescription = "MOD_WPN_" + options.NewName.ToUpperInvariant() + "_DESC";
            string newDisplayName_inv = "MOD_WPN_" + options.NewName.ToUpperInvariant() + "_INV";
            string newDescription_inv = "MOD_WPN_" + options.NewName.ToUpperInvariant() + "_INV_DESC";
            costumeNode.Element("Display_Name").Value = newDisplayName;
            costumeNode.Element("Description").Value = newDescription;
            inventoryItemNode.Element("DisplayName").Value = newDisplayName_inv;
            inventoryItemNode.Element("Description").Value = newDescription_inv;

            //COSTUME ENTRY EDITS
            string costumeName = costumeNode.Element("Name").Value;
            costumeNode.Element("Name").Value = options.NewName;

            var dlcElement = costumeNode.Element("Is_DLC");
            if (dlcElement != null)
            {
                string isDLCString = dlcElement.Value;
                if (isDLCString == "True")
                {
                    Console.WriteLine("Sorry, you can't clone DLC costumes. This is to prevent piracy.");
                    return;
                }
            }

            var costumeUnlockedElement = costumeNode.Element("Unlocked");
            if (costumeUnlockedElement != null)
            {
                //Automatically unlock cloned weapon costume
                costumeUnlockedElement.Value = "True";
            }
            else
            {
                costumeNode.Add(new XElement("Unlocked", "True"));
            }

            var costumeSlotIndexElement = costumeNode.Element("Costume_Slot_Index");

            if (costumeSlotIndexElement != null)
            {
                // Remove Costume_Slot_Index element so modded costumes don't conflict with eachother
                costumeSlotIndexElement.Remove();
            }


            //GET STR2s and edit stuff PER ITEM NODE
            int itemNodeCount = 0;
            string baseMeshName = "";
            bool found = false;

            foreach (var itemNode in allItemNodes)
            {
                //bool isStaticMesh = false;
                string oldMeshName = "";
                string newUniversalName = "";

                XElement meshInformationNode = null;
                if (itemNode.Value.Element("character_mesh") != null)
                {
                    meshInformationNode = itemNode.Value.Element("character_mesh");
                }

                if (meshInformationNode == null)
                {
                    var staticMeshNode = itemNode.Value.Element("Mesh");
                    var smeshFilenameNode = staticMeshNode.Element("Filename");
                    string smeshFilename = smeshFilenameNode.Value;
                    oldMeshName = Path.GetFileNameWithoutExtension(smeshFilename);

                    newUniversalName = (itemNodeCount == 0) ? options.NewName : ReplaceCaseInsensitive(oldMeshName, baseMeshName, options.NewName);
                    smeshFilenameNode.Value = newUniversalName + ".smeshx";

                    Console.WriteLine("Mapping mesh {0} -> {1}", smeshFilename, smeshFilenameNode.Value);

                    //isStaticMesh = true;
                }
                else
                {
                    var characterMeshNode = meshInformationNode.Element("character_mesh");
                    var cmeshFilenameNode = characterMeshNode.Element("Filename");
                    string cmeshFilename = cmeshFilenameNode.Value;
                    oldMeshName = Path.GetFileNameWithoutExtension(cmeshFilename);

                    newUniversalName = (itemNodeCount == 0) ? options.NewName : ReplaceCaseInsensitive(oldMeshName, baseMeshName, options.NewName);
                    cmeshFilenameNode.Value = newUniversalName + ".cmeshx";

                    var rigNode = meshInformationNode.Element("rig");
                    var rigFilenameNode = rigNode.Element("Filename");
                    string rigFilename = rigFilenameNode.Value;

                    rigFilenameNode.Value = newUniversalName + ".rigx";

                    Console.WriteLine("Mapping mesh {0} -> {1}", cmeshFilename, cmeshFilenameNode.Value);
                    Console.WriteLine("Mapping rig {0} -> {1}", rigFilename, rigFilenameNode.Value);
                }

                if (itemNodeCount == 0)
                {
                    baseMeshName = oldMeshName;
                }

                string oldMeshStr2 = "";
                if (itemNode.Value.Element("streaming_category").Value.Contains("Permanent"))
                {
                    oldMeshStr2 = itemNode.Value.Element("Name").Value + ".str2_pc";
                }
                else
                {
                    oldMeshStr2 = costumeEntry + ".str2_pc";
                }
                string oldTextureStr2 = oldMeshName + "_high.str2_pc";

                string newMeshName = newUniversalName;
                string newTextureName = newUniversalName + "_high";

                string newMeshStr2 = newMeshName + ".str2_pc";
                string newTextureStr2 = newTextureName + ".str2_pc";

                bool foundMesh = ClonePackfile(sriv, oldMeshStr2, newAsm_preload, newMeshName, Path.Combine(outputFolder, newMeshStr2));
                bool foundTexture = ClonePackfile(sriv, oldTextureStr2, newAsm_items, newTextureName, Path.Combine(outputFolder, newTextureStr2));

                if (foundMesh && itemNodeCount == 0)
                {
                    found = true;
                }

                itemNode.Value.Element("Name").Value = newUniversalName;

                if (itemNodeCount > 0)
                {
                    allItemNodes[0].Descendants("Props").Descendants("Prop").ElementAt(itemNodeCount - 1)
                         .Element("Name").Value = newUniversalName;
                }

                itemNodeCount++;
            }
            //FINAL TABLE EDITS
            //weapon_costumes.xtbl
            costumeNode.Element("Weapon_Entry").Value = options.NewName;
            costumeNode.Element("Item_Entry").Value = options.NewName;
            costumeNode.Element("Inventory_Entry").Value = options.NewName;

            //weapons.xtbl
            weaponNode.Element("Name").Value = options.NewName;

            //items_inventory.xtbl
            inventoryItemNode.Element("Name").Value = options.NewName;

            if (inventoryItemNode.Element("Info_Slot_Index") != null)
            {
                inventoryItemNode.Element("Info_Slot_Index").Remove();
            }

            //weapon_upgrades.xtbl
            if (weaponUpgradeNodes != null)
            {
                foreach (var node in weaponUpgradeNodes)
                {
                    node.Element("W_Info").Value = options.NewName;
                }
            }

            //store_weapons.xtbl
            storeWeaponNode.Element("Weapon").Value = options.NewName;

            //XTBL OUTPUT
            if (found)
            {
                foreach (var itemNode in allItemNodes)
                {
                    items_3d_Table.Add(itemNode.Value);
                }
                items_inventory_Table.Add(inventoryItemNode);
                store_weapons_WeaponsList.Add(storeWeaponNode);
                weapon_costumes_Table.Add(costumeNode);
                weapons_Table.Add(weaponNode);
                if (weaponUpgradeNodes != null)
                {
                    foreach (var node in weaponUpgradeNodes)
                    {
                        weapon_upgrades_Table.Add(node);
                    }
                }
                //for i in weaponSkinNodes -> add to respective table
            }

            //write out all xtbl objects to files
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "items_3d.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    items_3d.Save(writer);
                }
            }
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "items_inventory.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    items_inventory.Save(writer);
                }
            }
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "store_weapons.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    store_weapons.Save(writer);
                }
            }
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapon_costumes.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapon_costumes.Save(writer);
                }
            }
            /*using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapon_skins.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapon_skins.Save(writer);
                }
            }*/
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapon_upgrades.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapon_upgrades.Save(writer);
                }
            }
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapons.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapons.Save(writer);
                }
            }

            //write out all asm objects to files
            using (Stream itemsAsmOutStream = File.Create(Path.Combine(outputFolder, "items_containers.asm_pc")))
            {
                newAsm_items.Save(itemsAsmOutStream);
            }
            using (Stream preloadAsmOutStream = File.Create(Path.Combine(outputFolder, "items_preload_containers.asm_pc")))
            {
                newAsm_preload.Save(preloadAsmOutStream);
            }
            using (Stream costumesAsmOutStream = File.Create(Path.Combine(outputFolder, "mods_costumes.asm_pc")))
            {
                newAsm_costumes.Save(costumesAsmOutStream);
            }
            /*using (Stream skinsAsmOutStream = File.Create(Path.Combine(outputFolder, "mods_skins.asm_pc")))
            {
                newAsm_skins.Save(skinsAsmOutStream);
            }*/

            //String stuff-------------------------------------------------------------------

            string stringXmlFolder = Path.Combine(outputFolder, "stringxml");
            Directory.CreateDirectory(stringXmlFolder);

            foreach (var pair in languageStrings)
            {
                Language language = pair.Key;
                Dictionary<uint, string> strings = pair.Value;

                StringFile file = new StringFile(language, sriv);

                string newDisplayNameString = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDisplayStringCrc))
                {
                    string originalDisplayNameString = strings[originalDisplayStringCrc];
                    newDisplayNameString = "CLONE: " + originalDisplayNameString;
                }
                else
                {
                    Console.WriteLine("Warning: original language name string could not be found for {0}.", language);
                }
                string newDescriptionString = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDescriptionStringCrc))
                {
                    string originalDescriptionString = strings[originalDescriptionStringCrc];
                    newDescriptionString = "CLONED DESCRIPTION: " + originalDescriptionString;
                }
                else
                {
                    Console.WriteLine("Warning: original language description string could not be found for {0}.", language);
                }
                string newDisplayNameString_inv = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDisplayString_inv_Crc))
                {
                    string originalDisplayNameString_inv = strings[originalDisplayString_inv_Crc];
                    newDisplayNameString_inv = "CLONE: " + originalDisplayNameString_inv;
                }
                else
                {
                    Console.WriteLine("Warning: original language inventory name string could not be found for {0}.", language);
                }
                string newDescriptionString_inv = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDescriptionString_inv_Crc))
                {
                    string originalDescriptionString_inv = strings[originalDescriptionString_inv_Crc];
                    newDescriptionString_inv = "CLONED DESCRIPTION: " + originalDescriptionString_inv;
                }
                else
                {
                    Console.WriteLine("Warning: original language inventory description string could not be found for {0}.", language);
                }


                file.AddString(newDisplayName, newDisplayNameString);
                file.AddString(newDescription, newDescriptionString);
                file.AddString(newDisplayName_inv, newDisplayNameString_inv);
                file.AddString(newDescription_inv, newDescriptionString_inv);

                string newFilename = Path.Combine(outputFolder, String.Format("{0}_{1}.le_strings", options.NewName, LanguageUtility.GetLanguageCode(language)));
                string newXmlFilename = Path.Combine(stringXmlFolder, String.Format("{0}_{1}.xml", options.NewName, LanguageUtility.GetLanguageCode(language)));

                using (Stream s = File.Create(newFilename))
                {
                    file.Save(s);
                }

                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = "\t";
                settings.NewLineChars = "\r\n";

                using (XmlWriter xml = XmlTextWriter.Create(newXmlFilename, settings))
                {
                    xml.WriteStartDocument();
                    xml.WriteStartElement("Strings");
                    xml.WriteAttributeString("Language", language.ToString());
                    xml.WriteAttributeString("Game", sriv.Game.ToString());

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDisplayName);
                    xml.WriteString(newDisplayNameString);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDescription);
                    xml.WriteString(newDescriptionString);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDisplayName_inv);
                    xml.WriteString(newDisplayNameString_inv);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDescription_inv);
                    xml.WriteString(newDescriptionString_inv);

                    xml.WriteEndElement(); // String

                    xml.WriteEndElement(); // Strings
                    xml.WriteEndDocument();
                }
            }

            Console.WriteLine("Finished cloning weapon costume {0} to {1}!", costumeEntry, options.NewName);
        }

        static void costumeToCostumeProgram(Options options)
        {

            if (options.Output == null)
            {
                options.Output = options.NewName;
            }

            string outputFolder = options.Output;

            Directory.CreateDirectory(outputFolder);

            IGameInstance sriv = GameInstance.GetFromSteamId(GameSteamID.SaintsRowIV);
            LoadStrings(sriv);

            IAssetAssemblerFile newAsm_items; //items_containers.asm_pc
            IAssetAssemblerFile newAsm_preload; //items_containers.asm_pc
            IAssetAssemblerFile newAsm_costumes; //mods_costumes.asm_pc
            IAssetAssemblerFile newAsm_skins; //mods_skins.asm_pc

            //Make instances of template_items_containers.asm_pc, add containers in the middle, write them out to files at the end of the algorithm
            using (Stream newAsm_items_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_items = AssetAssemblerFile.FromStream(newAsm_items_Stream);
            }
            using (Stream newAsm_preload_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_preload = AssetAssemblerFile.FromStream(newAsm_preload_Stream);
            }
            using (Stream newAsm_costumes_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_costumes = AssetAssemblerFile.FromStream(newAsm_costumes_Stream);
            }
            using (Stream newAsm_skins_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_skins = AssetAssemblerFile.FromStream(newAsm_skins_Stream);
            }

            XDocument items_3d = null;
            XDocument items_inventory = null;
            XDocument weapon_costumes = null;
            XDocument weapon_skins = null;

            //Make instances of template_table.xtbl, add entries in the middle, write them out to files at the end of the algorithm
            using (Stream xtblTemplateStream = File.OpenRead(Path.Combine("templates", "template_table.xtbl")))
            {
                items_3d = XDocument.Load(xtblTemplateStream);
                items_inventory = new XDocument(items_3d);
                weapon_costumes = new XDocument(items_3d);
                weapon_skins = new XDocument(items_3d);
            }

            var items_3d_Table = items_3d.Descendants("Table").First();
            var items_inventory_Table = items_inventory.Descendants("Table").First();
            var weapon_costumes_Table = weapon_costumes.Descendants("Table").First();
            var weapon_skins_Table = weapon_skins.Descendants("Table").First();

            //This is where shit gets serious
            XElement costumeNode = FindWeaponCostume(options.Source, sriv);
            string costumeEntry = "";
            if (costumeNode != null)
            {
                costumeEntry = costumeNode.Element("Name").Value;
            }
            else
            {
                Console.WriteLine(String.Format("Couldn't find weapon costume \"{0}\"", options.Source));
                return;
            }
            string weaponEntry = costumeNode.Element("Weapon_Entry").Value; Console.WriteLine("Weapon: " + weaponEntry);
            string item3dEntry = costumeNode.Element("Item_Entry").Value; Console.WriteLine("3D Item: " + item3dEntry);
            string inventoryEntry = costumeNode.Element("Inventory_Entry").Value; Console.WriteLine("Inventory Item: " + inventoryEntry);

            XElement inventoryItemNode = FindInventoryItem(inventoryEntry, sriv);
            XElement item3dNode = FindItem3d(item3dEntry, sriv);
            List<XElement> skinNodes = FindCostumeSkins(options.Source, sriv);

            //CHECK IF ALL NODES WERE FOUND
            if (costumeNode == null)
            {
                Console.WriteLine("Couldn't find costume \"{0}\".", options.Source);
                return;
            }
            else if (inventoryItemNode == null)
            {
                Console.WriteLine("Couldn't find inventory item \"{0}\". Cloning inventory item \"Pistol-Gang\"", inventoryEntry);
                inventoryItemNode = FindInventoryItem("Pistol-Gang", sriv);
            }
            else if (item3dNode == null)
            {
                Console.WriteLine("Couldn't find item \"{0}\".", item3dEntry);
                return;
            }

            Dictionary<int, XElement> allItemNodes = new Dictionary<int, XElement>();
            allItemNodes.Add(0, item3dNode);

            //GET ALL PROP ITEMS
            int v = 1;
            foreach (XElement propElement in item3dNode.Descendants("Props").Descendants("Prop"))
            {
                string propItemEntry = "";
                if (propElement.Element("Name") != null)
                {
                    propItemEntry = propElement.Element("Name").Value;
                    Console.WriteLine(String.Format("Found prop: {0}", propItemEntry));
                }
                XElement propNode = FindItem3d(propItemEntry, sriv);
                if (propNode != null)
                {
                    allItemNodes.Add(v, propNode);
                }
                v++;

            }

            //SOME STRING STUFF IN ADVANCE
            if (costumeNode.Element("Display_Name") == null)
            {
                costumeNode.Add(new XElement("Display_Name", "CUST_WPN_COSTUME_DESC_LTPISTOL_0"));
            }

            if (inventoryItemNode.Element("DisplayName") == null)
            {
                inventoryItemNode.Add(new XElement("Display_Name", "CUST_WPN_COSTUME_DESC_LTPISTOL_0"));
            }

            if (costumeNode.Element("Description") == null)
            {
                costumeNode.Add(new XElement("Description", "CUST_WPN_COSTUME_DESC_LTPISTOL_0_DESC"));
            }

            if (inventoryItemNode.Element("Description") == null)
            {
                inventoryItemNode.Add(new XElement("Description", "CUST_WPN_COSTUME_DESC_LTPISTOL_0_DESC"));
            }

            string originalDisplayName = costumeNode.Element("Display_Name").Value;
            string originalDescription = costumeNode.Element("Description").Value;
            string originalDisplayName_inv = inventoryItemNode.Element("DisplayName").Value;
            string originalDescription_inv = inventoryItemNode.Element("Description").Value;
            uint originalDisplayStringCrc = Hashes.CrcVolition(originalDisplayName);
            uint originalDescriptionStringCrc = Hashes.CrcVolition(originalDescription);
            uint originalDisplayString_inv_Crc = Hashes.CrcVolition(originalDisplayName_inv);
            uint originalDescriptionString_inv_Crc = Hashes.CrcVolition(originalDescription_inv);
            string newDisplayName = "MOD_COST_" + options.NewName.ToUpperInvariant();
            string newDescription = "MOD_COST_" + options.NewName.ToUpperInvariant() + "_DESC";
            string newDisplayName_inv = "MOD_COST_" + options.NewName.ToUpperInvariant() + "_INV";
            string newDescription_inv = "MOD_COST_" + options.NewName.ToUpperInvariant() + "_INV_DESC";
            costumeNode.Element("Display_Name").Value = newDisplayName;
            costumeNode.Element("Description").Value = newDescription;
            inventoryItemNode.Element("DisplayName").Value = newDisplayName_inv;
            inventoryItemNode.Element("Description").Value = newDescription_inv;

            //COSTUME ENTRY EDITS
            string costumeName = costumeNode.Element("Name").Value;
            costumeNode.Element("Name").Value = options.NewName;

            var dlcElement = costumeNode.Element("Is_DLC");
            if (dlcElement != null)
            {
                string isDLCString = dlcElement.Value;
                if (isDLCString == "True")
                {
                    Console.WriteLine("Sorry, you can't clone DLC costumes. This is to prevent piracy.");
                    return;
                }
            }

            var costumeUnlockedElement = costumeNode.Element("Unlocked");
            if (costumeUnlockedElement != null)
            {
                //Automatically unlock cloned weapon costume
                costumeUnlockedElement.Value = "True";
            }
            else
            {
                costumeNode.Add(new XElement("Unlocked", "True"));
            }

            var costumeSlotIndexElement = costumeNode.Element("Costume_Slot_Index");

            if (costumeSlotIndexElement != null)
            {
                // Remove Costume_Slot_Index element so modded costumes don't conflict with eachother
                costumeSlotIndexElement.Remove();
            }


            //GET STR2s and edit stuff PER ITEM NODE
            int itemNodeCount = 0;
            string baseMeshName = "";
            bool found = false;

            foreach (var itemNode in allItemNodes)
            {
                //bool isStaticMesh = false;
                string oldMeshName = "";
                string newUniversalName = "";

                XElement meshInformationNode = null;
                if (itemNode.Value.Element("character_mesh") != null)
                {
                    meshInformationNode = itemNode.Value.Element("character_mesh");
                }

                if (meshInformationNode == null)
                {
                    var staticMeshNode = itemNode.Value.Element("Mesh");
                    var smeshFilenameNode = staticMeshNode.Element("Filename");
                    string smeshFilename = smeshFilenameNode.Value;
                    oldMeshName = Path.GetFileNameWithoutExtension(smeshFilename);

                    newUniversalName = (itemNodeCount == 0) ? options.NewName : ReplaceCaseInsensitive(oldMeshName, baseMeshName, options.NewName);
                    smeshFilenameNode.Value = newUniversalName + ".smeshx";

                    Console.WriteLine("Mapping mesh {0} -> {1}", smeshFilename, smeshFilenameNode.Value);

                    //isStaticMesh = true;
                }
                else
                {
                    var characterMeshNode = meshInformationNode.Element("character_mesh");
                    var cmeshFilenameNode = characterMeshNode.Element("Filename");
                    string cmeshFilename = cmeshFilenameNode.Value;
                    oldMeshName = Path.GetFileNameWithoutExtension(cmeshFilename);

                    newUniversalName = (itemNodeCount == 0) ? options.NewName : ReplaceCaseInsensitive(oldMeshName, baseMeshName, options.NewName);
                    cmeshFilenameNode.Value = newUniversalName + ".cmeshx";

                    var rigNode = meshInformationNode.Element("rig");
                    var rigFilenameNode = rigNode.Element("Filename");
                    string rigFilename = rigFilenameNode.Value;

                    rigFilenameNode.Value = newUniversalName + ".rigx";

                    Console.WriteLine("Mapping mesh {0} -> {1}", cmeshFilename, cmeshFilenameNode.Value);
                    Console.WriteLine("Mapping rig {0} -> {1}", rigFilename, rigFilenameNode.Value);
                }

                if (itemNodeCount == 0)
                {
                    baseMeshName = oldMeshName;
                }

                string oldMeshStr2 = "";
                if (itemNode.Value.Element("streaming_category").Value.Contains("Permanent"))
                {
                    oldMeshStr2 = itemNode.Value.Element("Name").Value + ".str2_pc";
                }
                else
                {
                    oldMeshStr2 = costumeEntry + ".str2_pc";
                }
                string oldTextureStr2 = oldMeshName + "_high.str2_pc";

                string newMeshName = newUniversalName;
                string newTextureName = newUniversalName + "_high";

                string newMeshStr2 = newMeshName + ".str2_pc";
                string newTextureStr2 = newTextureName + ".str2_pc";

                bool foundMesh = ClonePackfile(sriv, oldMeshStr2, newAsm_preload, newMeshName, Path.Combine(outputFolder, newMeshStr2));
                bool foundTexture = ClonePackfile(sriv, oldTextureStr2, newAsm_items, newTextureName, Path.Combine(outputFolder, newTextureStr2));

                if (foundMesh && itemNodeCount == 0)
                {
                    found = true;
                }

                itemNode.Value.Element("Name").Value = newUniversalName;

                if (itemNodeCount > 0)
                {
                    allItemNodes[0].Descendants("Props").Descendants("Prop").ElementAt(itemNodeCount - 1)
                         .Element("Name").Value = newUniversalName;
                }

                itemNodeCount++;
            }
            //FINAL TABLE EDITS
            //weapon_costumes.xtbl
            costumeNode.Element("Item_Entry").Value = options.NewName;
            costumeNode.Element("Inventory_Entry").Value = options.NewName;

            //items_inventory.xtbl
            inventoryItemNode.Element("Name").Value = options.NewName;

            if (inventoryItemNode.Element("Info_Slot_Index") != null)
            {
                inventoryItemNode.Element("Info_Slot_Index").Remove();
            }

            //XTBL OUTPUT
            if (found)
            {
                foreach (var itemNode in allItemNodes)
                {
                    items_3d_Table.Add(itemNode.Value);
                }
                items_inventory_Table.Add(inventoryItemNode);
                weapon_costumes_Table.Add(costumeNode);
                //for i in weaponSkinNodes -> add to respective table
            }

            //write out all xtbl objects to files
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "items_3d.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    items_3d.Save(writer);
                }
            }
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "items_inventory.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    items_inventory.Save(writer);
                }
            }
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapon_costumes.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapon_costumes.Save(writer);
                }
            }
            /*using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapon_skins.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapon_skins.Save(writer);
                }
            }*/

            //write out all asm objects to files
            using (Stream itemsAsmOutStream = File.Create(Path.Combine(outputFolder, "items_containers.asm_pc")))
            {
                newAsm_items.Save(itemsAsmOutStream);
            }
            using (Stream preloadAsmOutStream = File.Create(Path.Combine(outputFolder, "items_preload_containers.asm_pc")))
            {
                newAsm_preload.Save(preloadAsmOutStream);
            }
            using (Stream costumesAsmOutStream = File.Create(Path.Combine(outputFolder, "mods_costumes.asm_pc")))
            {
                newAsm_costumes.Save(costumesAsmOutStream);
            }
            /*using (Stream skinsAsmOutStream = File.Create(Path.Combine(outputFolder, "mods_skins.asm_pc")))
            {
                newAsm_skins.Save(skinsAsmOutStream);
            }*/

            //String stuff-------------------------------------------------------------------

            string stringXmlFolder = Path.Combine(outputFolder, "stringxml");
            Directory.CreateDirectory(stringXmlFolder);

            foreach (var pair in languageStrings)
            {
                Language language = pair.Key;
                Dictionary<uint, string> strings = pair.Value;

                StringFile file = new StringFile(language, sriv);

                string newDisplayNameString = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDisplayStringCrc))
                {
                    string originalDisplayNameString = strings[originalDisplayStringCrc];
                    newDisplayNameString = "CLONE: " + originalDisplayNameString;
                }
                else
                {
                    Console.WriteLine("Warning: original language name string could not be found for {0}.", language);
                }
                string newDescriptionString = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDescriptionStringCrc))
                {
                    string originalDescriptionString = strings[originalDescriptionStringCrc];
                    newDescriptionString = "CLONED DESCRIPTION: " + originalDescriptionString;
                }
                else
                {
                    Console.WriteLine("Warning: original language description string could not be found for {0}.", language);
                }
                string newDisplayNameString_inv = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDisplayString_inv_Crc))
                {
                    string originalDisplayNameString_inv = strings[originalDisplayString_inv_Crc];
                    newDisplayNameString_inv = "CLONE: " + originalDisplayNameString_inv;
                }
                else
                {
                    Console.WriteLine("Warning: original language inventory name string could not be found for {0}.", language);
                }
                string newDescriptionString_inv = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDescriptionString_inv_Crc))
                {
                    string originalDescriptionString_inv = strings[originalDescriptionString_inv_Crc];
                    newDescriptionString_inv = "CLONED DESCRIPTION: " + originalDescriptionString_inv;
                }
                else
                {
                    Console.WriteLine("Warning: original language inventory description string could not be found for {0}.", language);
                }


                file.AddString(newDisplayName, newDisplayNameString);
                file.AddString(newDescription, newDescriptionString);
                file.AddString(newDisplayName_inv, newDisplayNameString_inv);
                file.AddString(newDescription_inv, newDescriptionString_inv);

                string newFilename = Path.Combine(outputFolder, String.Format("{0}_{1}.le_strings", options.NewName, LanguageUtility.GetLanguageCode(language)));
                string newXmlFilename = Path.Combine(stringXmlFolder, String.Format("{0}_{1}.xml", options.NewName, LanguageUtility.GetLanguageCode(language)));

                using (Stream s = File.Create(newFilename))
                {
                    file.Save(s);
                }

                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = "\t";
                settings.NewLineChars = "\r\n";

                using (XmlWriter xml = XmlTextWriter.Create(newXmlFilename, settings))
                {
                    xml.WriteStartDocument();
                    xml.WriteStartElement("Strings");
                    xml.WriteAttributeString("Language", language.ToString());
                    xml.WriteAttributeString("Game", sriv.Game.ToString());

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDisplayName);
                    xml.WriteString(newDisplayNameString);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDescription);
                    xml.WriteString(newDescriptionString);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDisplayName_inv);
                    xml.WriteString(newDisplayNameString_inv);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDescription_inv);
                    xml.WriteString(newDescriptionString_inv);

                    xml.WriteEndElement(); // String

                    xml.WriteEndElement(); // Strings
                    xml.WriteEndDocument();
                }
            }

            Console.WriteLine("Finished cloning weapon costume {0} to {1}!", costumeEntry, options.NewName);
        }

        static void skinToSkinProgram(Options options)
        {

            if (options.Output == null)
            {
                options.Output = options.NewName;
            }

            string outputFolder = options.Output;

            Directory.CreateDirectory(outputFolder);

            IGameInstance sriv = GameInstance.GetFromSteamId(GameSteamID.SaintsRowIV);
            LoadStrings(sriv);

            IAssetAssemblerFile newAsm_skins; //mods_skins.asm_pc

            //Make instances of template_items_containers.asm_pc, add containers in the middle, write them out to files at the end of the algorithm
            using (Stream newAsm_skins_Stream = File.OpenRead(Path.Combine("templates", "template_items_containers.asm_pc")))
            {
                newAsm_skins = AssetAssemblerFile.FromStream(newAsm_skins_Stream);
            }

            XDocument weapon_skins = null;

            //Make instances of template_table.xtbl, add entries in the middle, write them out to files at the end of the algorithm
            using (Stream xtblTemplateStream = File.OpenRead(Path.Combine("templates", "template_table.xtbl")))
            {
                weapon_skins = XDocument.Load(xtblTemplateStream);
            }

            var weapon_skins_Table = weapon_skins.Descendants("Table").First();

            //This is where shit gets serious
            XElement skinNode = FindCostumeSkin(options.Source, sriv);

            string skinEntry = "";
            if (skinNode != null)
            {
                skinEntry = skinNode.Element("Name").Value;
            }
            else
            {
                Console.WriteLine(String.Format("Couldn't find skin \"{0}\"", options.Source));
                return;
            }

            //SOME STRING STUFF IN ADVANCE
            if (skinNode.Element("Display_Name") == null)
            {
                skinNode.Add(new XElement("Display_Name", "CUST_WPN_COSTUME_DESC_LTPISTOL_0"));
            }

            if (skinNode.Element("Description") == null)
            {
                skinNode.Add(new XElement("Description", "CUST_WPN_COSTUME_DESC_LTPISTOL_0_DESC"));
            }

            string originalDisplayName = skinNode.Element("Display_Name").Value;
            string originalDescription = skinNode.Element("Description").Value;
            uint originalDisplayStringCrc = Hashes.CrcVolition(originalDisplayName);
            uint originalDescriptionStringCrc = Hashes.CrcVolition(originalDescription);
            string newDisplayName = "MOD_SKN_" + options.NewName.ToUpperInvariant();
            string newDescription = "MOD_SKN_" + options.NewName.ToUpperInvariant() + "_DESC";
            skinNode.Element("Display_Name").Value = newDisplayName;
            skinNode.Element("Description").Value = newDescription;

            //SKIN ENTRY EDITS
            string skinName = skinEntry;
            skinNode.Element("Name").Value = options.NewName;


            var skinUnlockedElement = skinNode.Element("Unlocked");
            if (skinUnlockedElement != null)
            {
                //Automatically unlock cloned weapon costume
                skinUnlockedElement.Value = "True";
            }
            else
            {
                skinNode.Add(new XElement("Unlocked", "True"));
            }


            //GET STR2s and CLONE THEM

            string oldTextureName = "";

            if (skinNode.Element("Material_Library") != null)
            {
                var materialNode = skinNode.Element("Material_Library").Element("Filename");
                string oldMaterialName = materialNode.Value;
                oldTextureName = Path.GetFileNameWithoutExtension(oldMaterialName);

                materialNode.Value = options.NewName + ".matlibx";

                Console.WriteLine("Mapping mesh {0} -> {1}", oldMaterialName, materialNode.Value);
            }
            else
            {
                Console.WriteLine("Couldn't find Material_Library for skin \"{0}\"", skinName);
            }

            string oldSkinStr2 = oldTextureName + ".str2_pc";
            string newTextureName = options.NewName;
            string newSkinStr2 = options.NewName + ".str2_pc";

            bool foundSkin = ClonePackfile(sriv, oldSkinStr2, newAsm_skins, newTextureName, Path.Combine(outputFolder, newSkinStr2));

            if (foundSkin)
            {
                weapon_skins_Table.Add(skinNode);
            }

            //XTBL OUTPUT
            //write out skin xtbl to file
            using (Stream xtblOutStream = File.Create(Path.Combine(outputFolder, "weapon_skins.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    weapon_skins.Save(writer);
                }
            }

            //write out skin asm to file
            using (Stream skinsAsmOutStream = File.Create(Path.Combine(outputFolder, "mods_skins.asm_pc")))
            {
                newAsm_skins.Save(skinsAsmOutStream);
            }

            //String stuff-------------------------------------------------------------------

            string stringXmlFolder = Path.Combine(outputFolder, "stringxml");
            Directory.CreateDirectory(stringXmlFolder);

            foreach (var pair in languageStrings)
            {
                Language language = pair.Key;
                Dictionary<uint, string> strings = pair.Value;

                StringFile file = new StringFile(language, sriv);

                string newDisplayNameString = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDisplayStringCrc))
                {
                    string originalDisplayNameString = strings[originalDisplayStringCrc];
                    newDisplayNameString = "CLONE: " + originalDisplayNameString;
                }
                else
                {
                    Console.WriteLine("Warning: original language name string could not be found for {0}.", language);
                }
                string newDescriptionString = "CLONE: " + options.NewName;
                if (strings.ContainsKey(originalDescriptionStringCrc))
                {
                    string originalDescriptionString = strings[originalDescriptionStringCrc];
                    newDescriptionString = "CLONED DESCRIPTION: " + originalDescriptionString;
                }
                else
                {
                    Console.WriteLine("Warning: original language description string could not be found for {0}.", language);
                }

                file.AddString(newDisplayName, newDisplayNameString);
                file.AddString(newDescription, newDescriptionString);

                string newFilename = Path.Combine(outputFolder, String.Format("{0}_{1}.le_strings", options.NewName, LanguageUtility.GetLanguageCode(language)));
                string newXmlFilename = Path.Combine(stringXmlFolder, String.Format("{0}_{1}.xml", options.NewName, LanguageUtility.GetLanguageCode(language)));

                using (Stream s = File.Create(newFilename))
                {
                    file.Save(s);
                }

                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = "\t";
                settings.NewLineChars = "\r\n";

                using (XmlWriter xml = XmlTextWriter.Create(newXmlFilename, settings))
                {
                    xml.WriteStartDocument();
                    xml.WriteStartElement("Strings");
                    xml.WriteAttributeString("Language", language.ToString());
                    xml.WriteAttributeString("Game", sriv.Game.ToString());

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDisplayName);
                    xml.WriteString(newDisplayNameString);

                    xml.WriteEndElement(); // String

                    xml.WriteStartElement("String");

                    xml.WriteAttributeString("Name", newDescription);
                    xml.WriteString(newDescriptionString);

                    xml.WriteEndElement(); // String

                    xml.WriteEndElement(); // Strings
                    xml.WriteEndDocument();
                }
            }

            Console.WriteLine("Finished cloning skin {0} to {1}!", skinName, options.NewName);
        }

        //----------------------------MAIN-FUNCTION------------------------------

        static void Main(string[] args)
        {
            Options options = null;

            try
            {
                options = CommandLine.Parse<Options>();
            }
            catch (CommandLineException exception)
            {
                Console.WriteLine(exception.ArgumentHelp.Message);
                Console.WriteLine();
                Console.WriteLine(exception.ArgumentHelp.GetHelpText(Console.BufferWidth));
                return;
            }

            if (options.cloneFunction == "weapon")
            {
                costumeToWeaponProgram(options);
                return;
            }
            else if (options.cloneFunction == "costume")
            {
                costumeToCostumeProgram(options);
                return;
            }
            else if (options.cloneFunction == "skin")
            {
                skinToSkinProgram(options);
                return;
            }
            else
            {
                Console.WriteLine("Please specify a valid clone function:\n\"weapon\" - clones a COSTUME to a new WEAPON.\n\"costume\" - clones a COSTUME to a new COSTUME for the same weapon.\n\"skin\" - clones a SKIN to a new SKIN.");
                return;
            }
        }
    }
}
