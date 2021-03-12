using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

//#define USELDBTOOL

#if USELDBTOOL
using xiaoye97;
#endif

namespace com.brokenmass.plugin.DSP.MultiBuildUI
{
    public class Registry
    {
        //Local proto dictionaries
        public static Dictionary<int, ItemProto> items = new Dictionary<int, ItemProto>();
        private static Dictionary<int, int> itemUpgradeList = new Dictionary<int, int>();

        public static Dictionary<int, RecipeProto> recipes = new Dictionary<int, RecipeProto>();
        public static Dictionary<int, StringProto> strings = new Dictionary<int, StringProto>();

        public static Dictionary<int, TechProto> techs = new Dictionary<int, TechProto>();
        public static Dictionary<int, TechProto> techUpdateList = new Dictionary<int, TechProto>();

        public static Dictionary<int, ModelProto> models = new Dictionary<int, ModelProto>();
        public static Dictionary<string, Material[]> modelMats = new Dictionary<string, Material[]>();

        public static AssetBundle bundle;

        public static string vertaFolder = "";
        public static string keyWord = "";
        public static ManualLogSource LogSource;

        public static Action onLoadingFinished;


        private static int[] textureNames;

        /// <summary>
        /// Initialize Registry with needed data
        /// </summary>
        /// <param name="bundleName">Name of bundle to load</param>
        /// <param name="keyword">UNIQUE keyword of your mod</param>
        /// <param name="requireBundle">Do you need to load asset bundles</param>
        /// <param name="requireVerta">Do you need to load verta files</param>
        public static void Init(string bundleName, string keyword, bool requireBundle, bool requireVerta)
        {
            LogSource = Logger.CreateLogSource("Registry-" + keyword);
            keyWord = keyword;
            //get location of the plugin
            var assemblyLocation = Assembly.GetAssembly(typeof(Registry)).Location;
            string pluginfolder = Path.GetDirectoryName(assemblyLocation);


            int MainTex = Shader.PropertyToID("_MainTex");
            int NormalTex = Shader.PropertyToID("_NormalTex");
            int MSTex = Shader.PropertyToID("_MS_Tex");
            int EmissionTex = Shader.PropertyToID("_EmissionTex");

            textureNames = new[] { MainTex, NormalTex, MSTex, EmissionTex };


            FileInfo folder = new FileInfo($"{pluginfolder}/Verta/");
            FileInfo folder1 = new FileInfo($"{pluginfolder}/plugins/");
            if (Directory.Exists(folder.Directory?.FullName))
            {
                vertaFolder = pluginfolder;
            }
            else if (Directory.Exists(folder1.Directory?.FullName))
            {
                vertaFolder = $"{pluginfolder}/plugins";
            }
            else if (requireVerta)
            {
                vertaFolder = "";
                LogSource.LogError("Cannot find folder with verta files. Mod WILL not work!");
                return;
            }

            Debug.Log($"{pluginfolder}/{bundleName}");
            if (requireBundle)
            {
                //load assetbundle then load the prefab
                bundle = AssetBundle.LoadFromFile($"{pluginfolder}/{bundleName}");
            }
#if USELDBTOOL
            LDBTool.PostAddDataAction += onPostAdd;
            LDBTool.EditDataAction += EditProto;
#endif

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), keyword);
        }

        //Post register fixups
        private static void onPostAdd()
        {
            foreach (var kv in models)
            {
                kv.Value.Preload();
                PrefabDesc pdesc = kv.Value.prefabDesc;

                if (!modelMats.ContainsKey(kv.Value.PrefabPath)) continue;

                Material[] mats = modelMats[kv.Value.PrefabPath];

                //Material[] mats = pdesc.materials;
                for (int i = 0; i < pdesc.lodCount; i++)
                {
                    for (int j = 0; j < pdesc.lodMaterials[i].Length; j++)
                    {
                        pdesc.lodMaterials[i][j] = mats[j];
                    }
                }
            }

            foreach (var kv in items)
            {
                kv.Value.Preload(kv.Value.index);
            }

            foreach (var kv in recipes)
            {
                kv.Value.Preload(kv.Value.index);
            }

            foreach (var kv in techs)
            {
                kv.Value.Preload();
                kv.Value.Preload2();
            }

            foreach (var kv in techUpdateList)
            {
                TechProto OldTech = LDB.techs.Select(kv.Key);
                OldTech.postTechArray = OldTech.postTechArray.AddToArray(kv.Value);
            }

            onLoadingFinished?.Invoke();

            LogSource.LogInfo("Post loading is complete!");
        }

        private static void EditProto(Proto proto)
        {
            if (proto is ItemProto itemProto)
            {
                if (itemUpgradeList.ContainsKey(itemProto.ID))
                {
                    itemProto.Grade = itemUpgradeList[itemProto.ID];
                    LogSource.LogDebug("Changing grade of " + itemProto.name);
                }

                if (itemProto.Grade == 0 || items.ContainsKey(itemProto.ID)) return;

                foreach (var kv in items)
                {
                    if (kv.Value.Grade == 0 || kv.Value.Upgrades == null) continue;
                    if (itemProto.Grade > kv.Value.Upgrades.Length) continue;

                    if (kv.Value.Upgrades[itemProto.Grade - 1] == itemProto.ID)
                    {
                        itemProto.Upgrades = kv.Value.Upgrades;
                        LogSource.LogDebug("Updating upgrade list of " + itemProto.name);
                    }
                }
            }
        }

        //DO NOT use this function, i think it should be removed!
        private static int findAvailableID<T>(int startIndex, ProtoSet<T> set, Dictionary<int, T> list)
            where T : Proto
        {
            int id = startIndex;

            while (true)
            {
                if (!set.dataIndices.ContainsKey(id) && !list.ContainsKey(id))
                {
                    break;
                }

                if (id > 12000)
                {
                    LogSource.LogError("Failed to find free index!");
                    throw new ArgumentException("No free indices available!");
                }

                id++;
            }

            return id;
        }

        /// <summary>
        /// Creates custom material with given shader name.
        /// _MainTex ("Albedo (RGB) diffuse reflection (A) color mask", 2D)
        /// _NormalTex ("Normal map", 2D)
        /// _MS_Tex ("Metallic (R) transparent paste (G) metal (a) highlight", 2D)
        /// _EmissionTex ("Emission (RGB) self-luminous (A) jitter mask", 2D)
        /// </summary>
        /// <param name="shaderName">Name of shader to use</param>
        /// <param name="materialName">Name of finished material, can be anything</param>
        /// <param name="color">Tint color (In html format, #RRGGBBAA)</param>
        /// <param name="textures">Array of texture names in this order: albedo, normal, metallic, emission</param>
        /// <param name="keywords">Array of keywords to use</param>
        /// <param name="textureIDs">Array of texture property ids (Use Shader.PropertyToID)</param>
        public static Material CreateMaterial(string shaderName, string materialName, string color,
            string[] textures = null, string[] keywords = null, int[] textureIDs = null)
        {
            ColorUtility.TryParseHtmlString(color, out Color newCol);

            Material mainMat = new Material(Shader.Find(shaderName))
            {
                shaderKeywords = keywords ?? new[] { "_ENABLE_VFINST" },
                color = newCol,
                name = materialName
            };

            if (textures == null) return mainMat;
            int[] texIds = textureIDs ?? textureNames;

            for (int i = 0; i < textures.Length; i++)
            {
                if (i >= texIds.Length) continue;

                Texture2D texture = Resources.Load<Texture2D>(textures[i]);
                mainMat.SetTexture(texIds[i], texture);
            }

            return mainMat;
        }
#if USELDBTOOL
        //All of these register a specified proto in LDBTool

        /// <summary>
        /// Registers a ModelProto
        /// </summary>
        /// <param name="id">UNIQUE id of your model</param>
        /// <param name="proto">ItemProto which will be turned into building</param>
        /// <param name="prefabPath">Path to the prefab, starting from asset folder in your unity project</param>
        /// <param name="mats">List of materials to use</param>
        /// <param name="descFields">int Array of used description fields</param>
        /// <param name="buildIndex">Index in build Toolbar, FSS, F - first submenu, S - second submenu</param>
        /// <param name="grade">Grade of the building, used to add upgrading</param>
        /// <param name="upgradesIDs">List of buildings ids, that are upgradable to this one. You need to include all of them here in order. ID of this building should be zero</param>
        public static ModelProto registerModel(int id, ItemProto proto, string prefabPath, Material[] mats,
            int[] descFields, int buildIndex, int grade = 0, int[] upgradesIDs = null)
        {
            ModelProto model = new ModelProto
            {
                Name = id.ToString(),
                PrefabPath = prefabPath,
                ID = id
            };

            proto.Type = EItemType.Production;
            proto.ModelIndex = id;
            proto.ModelCount = 1;
            proto.BuildIndex = buildIndex;
            proto.BuildMode = 1;
            proto.IsEntity = true;
            proto.CanBuild = true;
            proto.DescFields = descFields;
            if (grade != 0 && upgradesIDs != null)
            {
                proto.Grade = grade;
                for (int i = 0; i < upgradesIDs.Length; i++)
                {
                    int itemID = upgradesIDs[i];
                    if (itemID == 0) continue;

                    itemUpgradeList.Add(itemID, i + 1);
                }

                upgradesIDs[grade - 1] = proto.ID;
                proto.Upgrades = upgradesIDs;
            }

            LDBTool.PreAddProto(ProtoType.Model, model);
            models.Add(model.ID, model);
            modelMats.Add(prefabPath, mats);

            return model;
        }

        /// <summary>
        /// Registers a ItemProto
        /// </summary>
        /// <param name="id">UNIQUE id of your item</param>
        /// <param name="name">LocalizedKey of name of the item</param>
        /// <param name="description">LocalizedKey of description of the item</param>
        /// <param name="iconPath">Path to icon, starting from assets folder of your unity project</param>
        /// <param name="gridIndex">Index in craft menu, format : PYXX, P - page</param>
        /// <param name="stackSize">Stack size of the item</param>
        public static ItemProto registerItem(int id, string name, string description, string iconPath,
            int gridIndex, int stackSize = 50)
        {
            //int id = findAvailableID(1001, LDB.items, items);

            ItemProto proto = new ItemProto
            {
                Type = EItemType.Material,
                StackSize = stackSize,
                FuelType = 0,
                IconPath = iconPath,
                Name = name,
                Description = description,
                GridIndex = gridIndex,
                DescFields = new[] {1},
                ID = id
            };

            LDBTool.PreAddProto(ProtoType.Item, proto);

            items.Add(proto.ID, proto);
            return proto;
        }

        /// <summary>
        /// Registers a RecipeProto
        /// </summary>
        /// <param name="id">UNIQUE id of your recipe</param>
        /// <param name="type">Recipe type</param>
        /// <param name="time">Time in ingame ticks. How long item is being made</param>
        /// <param name="input">Array of input IDs</param>
        /// <param name="inCounts">Array of input COUNTS</param>
        /// <param name="output">Array of output IDs</param>
        /// <param name="outCounts">Array of output COUNTS</param>
        /// <param name="description">LocalizedKey of description of this item</param>
        /// <param name="techID">Tech id, which unlock this recipe</param>
        public static RecipeProto registerRecipe(int id, ERecipeType type, int time, int[] input, int[] inCounts,
            int[] output,
            int[] outCounts, string description, int techID = 0)
        {
            if (output.Length > 0)
            {
                ItemProto first = items.ContainsKey(output[0]) ? items[output[0]] : LDB.items.Select(output[0]);

                TechProto tech = null;
                if (techID != 0 && LDB.techs.Exist(techID))
                {
                    tech = LDB.techs.Select(techID);
                }

                RecipeProto proto = new RecipeProto
                {
                    Type = type,
                    Handcraft = true,
                    TimeSpend = time,
                    Items = input,
                    ItemCounts = inCounts,
                    Results = output,
                    ResultCounts = outCounts,
                    Description = description,
                    GridIndex = first.GridIndex,
                    IconPath = first.IconPath,
                    Name = first.Name + "Recipe",
                    preTech = tech,
                    ID = id
                };

                LDBTool.PreAddProto(ProtoType.Recipe, proto);
                recipes.Add(id, proto);

                return proto;
            }

            throw new ArgumentException("Output array must not be empty");
        }


        /// <summary>
        /// Registers a TechProto for a technology.
        /// Total amount of each jello is calculated like this: N = H*C/3600, where H - total hash count, C - items per minute of jello.
        /// </summary>
        /// <param name="id"> UNIQUE ID of the technology. Note that if id > 2000 tech will be on upgrades page.</param>
        /// <param name="name">LocalizedKey of name of the tech</param>
        /// <param name="description">LocalizedKey of description of the tech</param>
        /// <param name="conclusion">LocalizedKey of conclusion of the tech upon completion</param>
        /// <param name="iconPath">Path to icon, starting from assets folder of your unity project</param>
        /// <param name="preTechs">Techs which lead to this tech</param>
        /// <param name="jellos">Items required to research the tech</param>
        /// <param name="jelloRate">Amount of items per minute required to research the tech</param>
        /// <param name="hashNeeded">Number of hashes needed required to research the tech</param>
        /// <param name="unlockRecipes">Once the technology has completed, what recipes are unlocked</param>
        /// <param name="position">Vector2 position of the technology on the technology screen</param>
        public static TechProto registerTech(int id, string name, string description, string conclusion,
            string iconPath, int[] preTechs, int[] jellos, int[] jelloRate, long hashNeeded,
            int[] unlockRecipes, Vector2 position)

        {
            bool isLabTech = jellos.Any(itemId => LabComponent.matrixIds.Contains(itemId));


            TechProto proto = new TechProto
            {
                ID = id,
                Name = name,
                Desc = description,
                Published = true,
                Conclusion = conclusion,
                IconPath = iconPath,
                IsLabTech = isLabTech,
                PreTechs = preTechs,
                Items = jellos,
                ItemPoints = jelloRate,
                HashNeeded = hashNeeded,
                UnlockRecipes = unlockRecipes,
                AddItems = new int[] { }, // what items to gift after research is done
                AddItemCounts = new int[] { },
                Position = position,
                PreTechsImplicit = new int[] { }, //Those funky implicit requirements
                UnlockFunctions = new int[] { }, //Upgrades.
                UnlockValues = new double[] { },
            };

            foreach (int tech in preTechs)
            {
                //Do not do LDB.techs.Select here, proto could be not added yet.
                techUpdateList.Add(tech, proto);
            }

            LDBTool.PreAddProto(ProtoType.Tech, proto);
            techs.Add(id, proto);

            return proto;
        }

        /// <summary>
        /// Registers a LocalizedKey
        /// </summary>
        /// <param name="key">UNIQUE key of your localizedKey</param>
        /// <param name="enTrans">English translation for this key</param>
        public static void registerString(string key, string enTrans)
        {
            int id = findAvailableID(100, LDB.strings, strings);

            StringProto proto = new StringProto
            {
                Name = key,
                ENUS = enTrans,
                ID = id
            };

            LDBTool.PreAddProto(ProtoType.String, proto);
            strings.Add(id, proto);
        }
#endif
    }


    [HarmonyPatch(typeof(UIBuildMenu), "StaticLoad")]
    static class UIBuildMenuPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemProto[,] ___protos)
        {
            foreach (var kv in Registry.items)
            {
                int buildIndex = kv.Value.BuildIndex;
                if (buildIndex > 0)
                {
                    int num = buildIndex / 100;
                    int num2 = buildIndex % 100;
                    if (num <= 12 && num2 <= 12)
                    {
                        ___protos[num, num2] = kv.Value;
                    }
                }
            }
        }
    }


    //Fix item stack size not working
    [HarmonyPatch(typeof(StorageComponent), "LoadStatic")]
    static class StorageComponentPatch
    {
        private static bool staticLoad;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!staticLoad)
            {
                foreach (var kv in Registry.items)
                {
                    StorageComponent.itemIsFuel[kv.Key] = (kv.Value.HeatValue > 0L);
                    StorageComponent.itemStackCount[kv.Key] = kv.Value.StackSize;
                }

                staticLoad = true;
            }
        }
    }

    //Loading custom resources
    [HarmonyPatch(typeof(Resources), "Load", new Type[] { typeof(string), typeof(Type) })]
    static class ResourcesPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string path, Type systemTypeInstance, ref UnityEngine.Object __result)
        {
            if (path.Contains(Registry.keyWord) && Registry.bundle != null)
            {
                if (Registry.bundle.Contains(path + ".prefab") && systemTypeInstance == typeof(GameObject))
                {
                    Material[] mats = Registry.modelMats[path];
                    UnityEngine.Object myPrefab = Registry.bundle.LoadAsset(path + ".prefab");
                    if (myPrefab != null)
                    {
                        Registry.LogSource.LogDebug("Loading known asset " + path +
                                                    $" ({(myPrefab != null ? "Success" : "Failure")})");

                        MeshRenderer[] renderers = ((GameObject)myPrefab).GetComponentsInChildren<MeshRenderer>();
                        foreach (MeshRenderer renderer in renderers)
                        {
                            Material[] newMats = new Material[renderer.sharedMaterials.Length];
                            for (int i = 0; i < newMats.Length; i++)
                            {
                                newMats[i] = mats[i];
                            }

                            renderer.sharedMaterials = newMats;
                        }
                    }

                    __result = myPrefab;
                    return false;
                }

                if (Registry.bundle.Contains(path + ".png"))
                {
                    UnityEngine.Object mySprite =
                        Registry.bundle.LoadAsset(path + ".png", systemTypeInstance);

                    Registry.LogSource.LogDebug("Loading known asset " + path +
                                                $" ({(mySprite != null ? "Success" : "Failure")})");

                    __result = mySprite;
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(VertaBuffer), "LoadFromFile")]
    static class VertaBufferPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string filename)
        {
            if (filename.Contains(Registry.keyWord) && !Registry.vertaFolder.Equals(""))
            {
                String newName = $"{Registry.vertaFolder}/{filename}";
                if (File.Exists(newName))
                {
                    filename = newName;
                    Registry.LogSource.LogDebug("Loading known verta file " + filename);
                }
            }

            return true;
        }
    }
}
