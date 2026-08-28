using System;

namespace CozyTown.Runtime.Livestock
{
    [Serializable]
    public readonly struct AnimalSnapshot
    {
        public AnimalSnapshot(string animalId, string speciesId, bool fedToday, bool productReady)
        {
            AnimalId = animalId;
            SpeciesId = speciesId;
            FedToday = fedToday;
            ProductReady = productReady;
        }

        public string AnimalId { get; }

        public string SpeciesId { get; }

        public bool FedToday { get; }

        public bool ProductReady { get; }
    }

    [Serializable]
    public sealed class LivestockSnapshot
    {
        public LivestockSnapshot(int lastProcessedDay, AnimalSnapshot[] animals)
        {
            LastProcessedDay = lastProcessedDay;
            Animals = animals == null || animals.Length == 0
                ? Array.Empty<AnimalSnapshot>()
                : (AnimalSnapshot[])animals.Clone();
        }

        public int LastProcessedDay { get; }

        public AnimalSnapshot[] Animals { get; }
    }
}
