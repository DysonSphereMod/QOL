using System;
using System.Linq;

namespace DefaultNamespace
{
    public static class ResearchTechHelper
    {
        private static TechProto _sprayLevel3Proto;
        private static TechProto _sprayLevel2Proto;
        private static TechProto _sprayLevel1Proto;

        public static float GetMaxProductivityIncrease()
        {
            var highestProliferatorTechUnlocked = GetMaxIncIndex();
            return (float)Cargo.incTableMilli[highestProliferatorTechUnlocked];
        }

        public static float GetMaxSpeedIncrease()
        {
            var highestProliferatorTechUnlocked = GetMaxIncIndex();
            return (float)Cargo.accTableMilli[highestProliferatorTechUnlocked];
        }

        private static int GetMaxIncIndex()
        {
            InitTechProtos();
            if (GameMain.history.techStates[_sprayLevel3Proto.ID].unlocked)
                return 4;
            if (GameMain.history.techStates[_sprayLevel2Proto.ID].unlocked)
                return 2;
            if (GameMain.history.techStates[_sprayLevel1Proto.ID].unlocked)
                return 1;
            return 0;
        }

        private static void InitTechProtos()
        {
            if (_sprayLevel3Proto == null)
            {
                var proliferatorProtos = LDB.techs.dataArray.ToList().FindAll(t => t.Name.Contains("增产剂"));
                proliferatorProtos.Sort((p1, p2) =>
                {
                    if (p1.PreTechs.Contains(p2.ID))
                    {
                        // sorting high to low
                        return -1;
                    }

                    if (p2.PreTechs.Contains(p1.ID))
                    {
                        return 1;
                    }

                    return p1.ID.CompareTo(p2.ID);
                });
                if (proliferatorProtos.Count >= 3)
                {
                    // if more are added, add them here
                    _sprayLevel3Proto = proliferatorProtos[0];
                    _sprayLevel2Proto = proliferatorProtos[1];
                    _sprayLevel1Proto = proliferatorProtos[2];
                }
            }
        }
    }
}
