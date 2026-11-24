using Game.Utils;
using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class AbilityId {
        public const string Move = nameof(Move);
        public const string Fly = nameof(Fly);
        public const string Build = nameof(Build);
        public const string Manipulate = nameof(Manipulate);
        public const string Carry = nameof(Carry);
        public const string Firefight = nameof(Firefight);
        public const string Sleep = nameof(Sleep);
        public const string Eat = nameof(Eat);
        public const string Chew = nameof(Chew);
        /// <summary>
        /// Eating like an animal
        /// </summary>
        public const string AnimalEat = nameof(AnimalEat);
        public const string CleanDevice = nameof(CleanDevice);
        public const string CleanFloors = nameof(CleanFloors);
        public const string Mine = nameof(Mine);
        public const string Recharge = nameof(Recharge);
        public const string UseToilet = nameof(UseToilet);
        public const string UseShower = nameof(UseShower);
        public const string IdleHide = nameof(IdleHide);
        public const string UseItem = nameof(UseItem);
        public const string RideBot = nameof(RideBot);
        public const string PetAnimal = nameof(PetAnimal);
        public const string FaceHug = nameof(FaceHug);
        public const string Research = nameof(Research);
        public const string DumbLabor = nameof(DumbLabor);
        public const string SkilledLabor = nameof(SkilledLabor);
        public const string Intelligence = nameof(Intelligence);
        public const string Vocalize = nameof(Vocalize);
        public const string Hear = nameof(Hear);
        public const string Comprehend = nameof(Comprehend);
    }
}
