namespace Game.Constants {
    // UDBT = UIDataBlockType
    // Numbers for old items shouldn't change in order to preserve mods
    // last number: 66 (DNoBgText) - change it here when you add something
    // REMINDER: when adding new THIN widget types, also update UDB.ConsiderThin
    // REMINDER: update UIDataBlockListView.CanGroup and check GetBlockHeight
    public enum UDBT : byte {
        // Prefix I (short for Info) means that there is a button that shows details
        IText = 0,
        ITextThin = 59,
        IProgress = 1,
        IProgressUnder = 64,
        IProgressStacked = 51,
        IProgressThin = 52,
        IProgress2Btn = 63,
        IProgressBtn = 2,
        IProgressBtnThin = 56,
        IPriority = 3,
        IEvent = 4,
        IEventSticky = 5,
        IEventInfo = 6,
        IEventInfoSticky = 7,
        IEventStickyAutoSize = 49,
        ///<summary>DO NOT USE THIS! it's being used automatically by Event Panel</summary>
        _IEventGroup = 8,
        ITextBtn = 9,
        ITextBtnThin = 55,
        ITextRB = 10,
        ITextBtnLong = 11,
        IEntity = 31, // without submenu
        IEntity2 = 32, // like ientity but with submenu / second button
        IEntityBig = 57, // big avatar + name + desc for beings
        IAmtInput = 33, // originally for the new trade view
        ITextAutoSize = 36,
        IImageAutoSize = 47,
        IRawImageAutoSize = 50,
        IJobMatrix = 44,
        IJobMatrixHeader = 45,
        // Prefix D (short for detail) means that there is no button on the left side of the block
        DLabelIcon = 12,
        DLabel = 13,
        DBtn = 14,
        DBtnPlain = 61,
        DBtnThin = 60,
        /// <summary>
        /// Button + Button
        /// </summary>
        DBtn2 = 43,
        /// <summary>
        /// Button + Icon Button
        /// </summary>
        DBtn3 = 48,
        DBtnInverse = 38,
        DBtnLong = 15,
        DProgress = 16,
        DProgressThin = 54,
        DProgressBtn = 17,
        DProgressIconBtn = 62,
        /// <summary>
        /// Custom data is Vector2 (min max)
        /// </summary>
        DSlider = 18,
        /// <summary>
        /// Custom data is Vector2 (min max)
        /// </summary>
        DSliderBtn = 35,
        DText = 19,
        DTextThin = 58,
        DNoBgText = 66,
        DTextInput = 39,
        /// <summary>
        /// Set CustomData to false (bool) to prevent rebuilding the UI on each keystroke
        /// </summary>
        DTextInputBtn = 40,
        DPriorityInput = 41,
        DToggle = 42,
        DTextAutoSize = 37,
        DTextBtnAutoSize = 65,
        DImageAutoSize = 46,
        DTextRB = 20,
        DTextRBHeader = 21,
        DTextRB2Header = 22,
        DTextBtn = 23,
        DTextBtnLong = 24,
        //[Obsolete("Use ITextBtn instead, this one is a bit obsolete")]
        DEntityBtnObsolete = 25,
        DPriority = 26,
        DChart = 27,
        DDropdown = 34,
        /// <summary>
        /// Set the value of this widget to 1f to prevent auto expand
        /// when left or right side is missing
        /// </summary>
        Pair = 28,
        /// <summary>
        /// No auto expand on this one
        /// </summary>
        Three = 53,
        // Special widget types, full screen
        FResearchTree = 29,
        FStarmap = 30,
    }
}
