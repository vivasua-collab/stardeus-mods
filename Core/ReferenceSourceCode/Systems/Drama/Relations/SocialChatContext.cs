namespace Game.Systems.Drama {
    public class SocialChatContext {
        public static readonly SocialChatContext NoChat = new SocialChatContext() {
            Forbidden = true,
        };
        public static SocialChatContext Create(string ctx, bool faceBuddy = true, int expectedDuration = 0) {
            return new SocialChatContext() {
                Context = ctx,
                FaceBuddy = faceBuddy,
                ExpectedDuration = expectedDuration,
            };
        }
        public string Context;
        /// <summary>
        /// If this is false, we will not face the chat buddy (interlocutor) when
        /// starting a conversation
        /// </summary>
        public bool FaceBuddy;
        public bool Forbidden;
        /// <summary>
        /// 0 = random between 1-5 minutes (default)
        /// </summary>
        public int ChatDelay;
        public int ExpectedDuration;
    }
}
