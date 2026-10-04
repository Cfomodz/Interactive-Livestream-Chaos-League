using System;

namespace LiveChat
{
    [Serializable]
    public class LiveChatEmote
    {
        public string Id;
        public string Name;
        public int StartIndex;
        public int EndIndex;
        public string ImageUrl;
    }
}
