namespace Game.Data {
    public struct NamedEntity : INamedEntity {
        public string EntityName { get; private set; }

        public static INamedEntity For(string name) {
            return new NamedEntity() {
                EntityName = name
            };
        }
    }
}
