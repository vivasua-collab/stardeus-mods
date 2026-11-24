namespace Game.Systems.Notify {
    public readonly struct NotificationHandle {
        internal readonly int Id;
        public bool IsValid => Id != 0;
        public NotificationHandle(int id) {
            Id = id;
        }
        public override string ToString() {
            return $"Notif[{Id}]";
        }
    }
}
