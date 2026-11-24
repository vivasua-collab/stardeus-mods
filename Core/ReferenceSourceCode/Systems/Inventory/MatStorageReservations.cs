using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Utils;

namespace Game.Components {
    public sealed class MatStorageReservations {
        public MatStorageReservations(IMatStorage s) {
            storage = s;
        }

        private readonly IMatStorage storage;

        // Keyed by (Being, MatType) to support multiple reservations per Being
        private readonly Dictionary<(Being, MatType), MatReservation> reservationsByKey = new();

        // For quick lookup of all reservations for a Being
        private readonly Dictionary<Being, List<MatReservation>> reservationsByBeing = new();

        // Total reserved amounts per MatType
        public readonly Dictionary<MatType, int> Mats = new();

        public int Count => reservationsByKey.Count;

        public bool HasAnyReservation(Being who) {
            return reservationsByBeing.ContainsKey(who);
        }

        public bool HasReservation(Being who, MatType type) {
            return reservationsByKey.ContainsKey((who, type));
        }

        public MatReservation GetReservation(Being who, MatType type) {
            return reservationsByKey.GetValueOrDefault((who, type));
        }

        public List<MatReservation> GetAllReservations(Being who) {
            return reservationsByBeing.GetValueOrDefault(who);
        }

        public void Add(MatReservation matRes) {
            var key = (matRes.Being, matRes.Type);

            if (reservationsByKey.ContainsKey(key)) {
                D.ErrSoft("Worker already has reservation for this mat type! {0} <- {1}",
                    storage.Entity, matRes);
                // Update existing instead of adding duplicate
                var existing = reservationsByKey[key];
                // Update Mats tracking
                if (Mats.TryGetValue(matRes.Type, out var amt)) {
                    amt -= existing.Amount;
                    amt += matRes.Amount;
                    Mats[matRes.Type] = amt;
                }
                existing.Amount = matRes.Amount;
                existing.Ctx = matRes.Ctx;
                existing.Since = matRes.Since;
                return;
            }

            reservationsByKey.Add(key, matRes);

            if (!reservationsByBeing.TryGetValue(matRes.Being, out var list)) {
                list = new List<MatReservation>(4);
                reservationsByBeing.Add(matRes.Being, list);
            }
            list.Add(matRes);

            if (Mats.TryGetValue(matRes.Type, out var currentAmt)) {
                Mats[matRes.Type] = currentAmt + matRes.Amount;
            } else {
                Mats.Add(matRes.Type, matRes.Amount);
            }
        }

        public bool Remove(Being who, MatType type) {
            var key = (who, type);
            if (!reservationsByKey.TryGetValue(key, out var matRes)) {
                return false;
            }

            reservationsByKey.Remove(key);

            if (reservationsByBeing.TryGetValue(who, out var list)) {
                list.Remove(matRes);
                if (list.Count == 0) {
                    reservationsByBeing.Remove(who);
                }
            }

            if (matRes.Type != null) {
                if (Mats.TryGetValue(matRes.Type, out var amt)) {
                    amt -= matRes.Amount;
                    if (amt > 0) {
                        Mats[matRes.Type] = amt;
                    } else {
                        Mats.Remove(matRes.Type);
                    }
                }
            }

            return true;
        }

        public int RemoveAll(Being who) {
            if (!reservationsByBeing.TryGetValue(who, out var list)) {
                return 0;
            }

            int removed = 0;
            // Iterate in reverse to safely remove while iterating
            for (int i = list.Count - 1; i >= 0; i--) {
                var matRes = list[i];
                var key = (who, matRes.Type);
                reservationsByKey.Remove(key);

                if (matRes.Type != null) {
                    if (Mats.TryGetValue(matRes.Type, out var amt)) {
                        amt -= matRes.Amount;
                        if (amt > 0) {
                            Mats[matRes.Type] = amt;
                        } else {
                            Mats.Remove(matRes.Type);
                        }
                    }
                }
                removed++;
            }

            reservationsByBeing.Remove(who);
            return removed;
        }

        // For iteration in UI, debugging, etc.
        public IEnumerable<MatReservation> AllReservations => reservationsByKey.Values;
    }
}
