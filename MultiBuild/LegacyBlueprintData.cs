using System;
using System.Collections.Generic;
using FullSerializer;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [Serializable]
    public class BeltCopy_V1
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
    public class BuildingCopy_V1
    {

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
    public class InserterCopy_V1
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

    [fsObject("1")]
    public class BlueprintData_V1
    {
        [NonSerialized] public string name = "";
        public int version = 1;
        public Vector3 referencePos = Vector3.zero;
        public Quaternion inverseReferenceRot = Quaternion.identity;
        public float referenceYaw = 0f;

        public Dictionary<int, BuildingCopy_V1> copiedBuildings = new Dictionary<int, BuildingCopy_V1>();
        public Dictionary<int, InserterCopy_V1> copiedInserters = new Dictionary<int, InserterCopy_V1>();
        public Dictionary<int, BeltCopy_V1> copiedBelts = new Dictionary<int, BeltCopy_V1>();
        
        public static Vector3 GetPointFromMoves(Vector3 from, Vector3[] moves, Quaternion fromRotation)
        {
            var targetPos = from;
            var planetAux = GameMain.data.mainPlayer.planetData.aux;
            // Note: rotates each move relative to the rotation of the from
            for (int i = 0; i < moves.Length; i++)
                targetPos = planetAux.Snap(targetPos + fromRotation * moves[i], true, false);

            return targetPos;
        }
    }
    
    public class Vector3Converter_V1 : fsDirectConverter<Vector3>
    {
        public const float JSON_PRECISION = 100f;
        public override Type ModelType => typeof(Vector3);

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
}
