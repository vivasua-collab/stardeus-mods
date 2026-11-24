namespace Game.Systems.Voice {
    /// <summary>
    /// Clip ids for spoken event callouts, played back by VoiceSys.
    /// Files live at Assets/Resources/Sounds/Voice/&lt;Name&gt;.ogg, moddable the
    /// same way as any other clip in The.Sounds.
    /// </summary>
    public static class VoiceLine {
        public const string Fire = "Voice/Fire";
        // Raiders inbound
        public const string Raid = "Voice/Raid";
        // Raiders aboard
        public const string Boarding = "Voice/Boarding";
        public const string BreachCapsule = "Voice/BreachCapsule";
        public const string Hullpiercer = "Voice/Hullpiercer";
        public const string Asteroid = "Voice/Asteroid";
        public const string MeteoroidShower = "Voice/MeteoroidShower";
        public const string MicroMeteoroids = "Voice/MicroMeteoroids";
        public const string MeteorImpact = "Voice/MeteorImpact";
        public const string EnemyShip = "Voice/EnemyShip";
        public const string Bomb = "Voice/Bomb";
        public const string ReactorMalfunction = "Voice/ReactorMalfunction";
        public const string StarProximity = "Voice/StarProximity";
    }
}
