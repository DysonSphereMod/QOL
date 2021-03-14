using FullSerializer;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;


namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [Serializable]
    public class BeltCopy
    {
        [NonSerialized]
        public ItemProto itemProto;

        public int protoId;
        public int originalId = 0;
        public Vector3 originalPos;
        public Quaternion originalRot;
        public int beltId;

        public Vector3 cursorRelativePos = Vector3.zero;
        public Vector3[] movesFromReference = new Vector3[0];

        public int backInputId;
        public int leftInputId;
        public int rightInputId;
        public int outputId;

        public int connectedBuildingId;
        public int connectedBuildingSlot;
        public bool connectedBuildingIsOutput;
    }



    [Serializable]
    public class BuildingCopy
    {
        [Serializable]
        public class StationSetting
        {
            //transport.SetStationStorage(this.station.id, this.index, this.station.storage[this.index].itemId, this.station.storage[this.index].max, suggestLogistic2, this.station.storage[this.index].remoteLogic, GameMain.mainPlayer.package);
            public int index;
            public int itemId;
            public int max;
            public ELogisticStorage localLogic;
            public ELogisticStorage remoteLogic;
        }

        [Serializable]
        public class SlotFilter
        {
            public int slotIndex;
            public int storageIdx;
        }

        [NonSerialized]
        public ItemProto itemProto;

        public int protoId;
        public int originalId = 0;
        public Vector3 originalPos;
        public Quaternion originalRot;

        public Vector3 cursorRelativePos = Vector3.zero;
        public Vector3[] movesFromReference = new Vector3[0];
        public float cursorRelativeYaw = 0f;
        public int modelIndex = 0;

        public int recipeId;

        public List<StationSetting> stationSettings = new List<StationSetting>();
        public List<SlotFilter> slotFilters = new List<SlotFilter>();
    }

    [Serializable]
    public class InserterCopy
    {
        [NonSerialized]
        public ItemProto itemProto;

        public int protoId;
        public int originalId = 0;

        public int pickTarget;
        public int insertTarget;

        public int referenceBuildingId = 0;

        public bool incoming;
        public int startSlot;
        public int endSlot;
        public Vector3 posDelta;
        public Vector3 pos2Delta;
        public Quaternion rot;
        public Quaternion rot2;
        public Vector3[] movesFromReference = new Vector3[0];
        public short pickOffset;
        public short insertOffset;
        public short t1;
        public short t2;
        public int filterId;
        public int refCount;
        public bool otherIsBelt;
    }

    // reduce vector3 position to 2 decimal digits to reduce blueprint size
    public class Vector3Converter : fsDirectConverter<Vector3>
    {
        public const float JSON_PRECISION = 100f;
        public override Type ModelType { get { return typeof(Vector3); } }

        public override object CreateInstance(fsData data, Type storageType)
        {
            return new Vector3();
        }

        protected override fsResult DoSerialize(Vector3 instance, Dictionary<string, fsData> serialized)
        {
            serialized["x"] = new fsData((float)Math.Round(((Vector3)instance).x * JSON_PRECISION) / JSON_PRECISION);
            serialized["y"] = new fsData((float)Math.Round(((Vector3)instance).y * JSON_PRECISION) / JSON_PRECISION);
            serialized["z"] = new fsData((float)Math.Round(((Vector3)instance).z * JSON_PRECISION) / JSON_PRECISION);

            return fsResult.Success;
        }

        protected override fsResult DoDeserialize(Dictionary<string, fsData> serialized, ref Vector3 model)
        {
            model.x = (float)serialized["x"].AsDouble;
            model.y = (float)serialized["y"].AsDouble;
            model.z = (float)serialized["z"].AsDouble;

            return fsResult.Success;
        }
    }

    public class BlueprintData
    {
        public int version = 1;
        public Vector3 referencePos = Vector3.zero;
        public Quaternion inverseReferenceRot = Quaternion.identity;
        public float referenceYaw = 0f;

        public Dictionary<int, BuildingCopy> copiedBuildings = new Dictionary<int, BuildingCopy>();
        public Dictionary<int, InserterCopy> copiedInserters = new Dictionary<int, InserterCopy>();
        public Dictionary<int, BeltCopy> copiedBelts = new Dictionary<int, BeltCopy>();

        public static BlueprintData Import(string input)
        {
            try
            {
                var unzipped = Unzip(Convert.FromBase64String(input));

                fsSerializer serializer = new fsSerializer();
                fsData data = fsJsonParser.Parse(unzipped);

                BlueprintData deserialized = null;

                serializer.TryDeserialize<BlueprintData>(data, ref deserialized).AssertSuccessWithoutWarnings();

                foreach (var building in deserialized.copiedBuildings.Values)
                {
                    building.itemProto = LDB.items.Select((int)building.protoId);
                }
                foreach (var belt in deserialized.copiedBelts.Values)
                {
                    belt.itemProto = LDB.items.Select((int)belt.protoId);
                }
                foreach (var inserter in deserialized.copiedInserters.Values)
                {
                    inserter.itemProto = LDB.items.Select((int)inserter.protoId);
                }

                return deserialized;
            }
            catch
            {
                return null;
            }
        }


        public string Export()
        {
            fsSerializer serializer = new fsSerializer();

            serializer.AddConverter(new Vector3Converter());
            serializer.TrySerialize<BlueprintData>(this, out fsData data).AssertSuccessWithoutWarnings();

            string json = fsJsonPrinter.CompressedJson(data);
            return Convert.ToBase64String(Zip(json));
        }

        private static void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }

        private static byte[] Zip(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }

        private static string Unzip(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    CopyTo(gs, mso);
                }

                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }
    }
}
