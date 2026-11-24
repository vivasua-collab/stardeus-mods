using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class JobTypeId {
        // Mindless, Physical
        public const string Cleaning = "Cleaning";
        // Violent, Internal, Unskilled, Dangerous
        public const string Combat = "Combat";
        // Physical
        public const string Construction = "Construction";
        // Physical
        public const string Cooking = "Cooking";
        // Intellectual, Creative, MicroPrecision
        public const string Crafting = "Crafting";
        // Physical, Mindless
        public const string Demolition = "Demolition";
        // Physical, Challenging, Dangerous
        public const string Expedition = "Expedition";
        // Physical, Challenging, Dangerous
        public const string Firefighting = "Firefighting";
        // Physical, Mindless, Unskilled
        public const string Hauling = "Hauling";
        // Internal, Unskilled
        public const string Idle = "Idle";
        // Caring, Intellectual
        public const string Medical = "Medical";
        // Violent, Physical, Dangerous
        public const string Melee = "Melee";
        // Physical, Mindless
        public const string Mining = "Mining";
        // Internal, Unskilled
        public const string Need = "Need";
        // Caring, Mindless
        public const string Plants = "Plants";
        // MicroPrecision, Intellectual
        public const string Piloting = "Piloting";
        // Physical
        public const string Repairs = "Repairs";
        // Physical, Unskilled
        public const string Rescue = "Rescue";
        // Intellectual, Creative, Challenging
        public const string Research = "Research";
        // Intellectual, MicroPrecision
        public const string Robotics = "Robotics";
        // Violent, Dangerous
        public const string Shooting = "Shooting";
        // Caring
        public const string Social = "Social";
        // Internal, Unskilled
        public const string Task = "Task";

    }
}
