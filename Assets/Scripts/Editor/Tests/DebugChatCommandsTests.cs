using System.Collections.Generic;
using LiveChat;
using NUnit.Framework;
using UnityEngine;

/// <summary>Debug chat's "/" commands raise the stream events the game handles, with consistent viewer IDs.</summary>
public class DebugChatCommandsTests
{
    private GameObject _gameObject;
    private LocalDebugLiveChatClient _chat;
    private readonly List<object> _events = new List<object>();

    private string _folder;

    [SetUp]
    public void SetUp()
    {
        _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ChaosLeagueDebugChatTests_" + System.Guid.NewGuid().ToString("N"));
        UserData.FolderOverride = _folder;
        AppConfig.Load(); // reward costs come from the config
        _gameObject = new GameObject("DebugChat");
        _chat = _gameObject.AddComponent<LocalDebugLiveChatClient>();
        _chat.MessageReceived += e => _events.Add(e);
        _chat.ChannelPointsRedeemed += e => _events.Add(e);
        _chat.Subscribed += e => _events.Add(e);
        _chat.SubscriptionGifted += e => _events.Add(e);
        _chat.CommunityGiftStarted += e => _events.Add(e);
        _chat.Raided += e => _events.Add(e);
        _events.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_gameObject);
        UserData.FolderOverride = null;
        if (System.IO.Directory.Exists(_folder))
            System.IO.Directory.Delete(_folder, true);
    }

    [Test]
    public void AsChatsAsAnotherViewer()
    {
        DebugChatCommands.Run(_chat, "/as Alice !invitedby @bob");

        LiveChatMessage message = (LiveChatMessage)_events[0];
        Assert.AreEqual("alice", message.UserId);
        Assert.AreEqual("!invitedby @bob", message.RawMessage);
        Assert.IsFalse(message.IsBroadcaster);
    }

    [Test]
    public void BitsCheerOnAMessage()
    {
        DebugChatCommands.Run(_chat, "/bits alice 300 !lava");

        LiveChatMessage message = (LiveChatMessage)_events[0];
        Assert.AreEqual(300, message.Bits);
        StringAssert.StartsWith("!lava", message.RawMessage);
    }

    [Test]
    public void RedeemUsesTheGamesRewardTitles()
    {
        DebugChatCommands.Run(_chat, "/redeem alice 100");
        DebugChatCommands.Run(_chat, "/redeem alice lava");

        LiveChatChannelPointsRedemption tickets = (LiveChatChannelPointsRedemption)_events[0];
        Assert.AreEqual(TwitchApi.BidRewardTitle(100), tickets.RewardTitle);
        Assert.AreEqual(100, tickets.Cost);
        LiveChatChannelPointsRedemption lava = (LiveChatChannelPointsRedemption)_events[1];
        Assert.AreEqual(TwitchApi.LavaRewardTitle, lava.RewardTitle);
        Assert.AreEqual(TwitchApi.LavaRewardCost, lava.Cost);
    }

    [Test]
    public void AViewersChatAndEventsAreTheSamePlayer()
    {
        DebugChatCommands.Run(_chat, "/as Alice hi");
        DebugChatCommands.Run(_chat, "/redeem ALICE 5");
        DebugChatCommands.Run(_chat, "/sub alice");

        Assert.AreEqual(((LiveChatMessage)_events[0]).UserId, ((LiveChatChannelPointsRedemption)_events[1]).UserId);
        Assert.AreEqual(((LiveChatMessage)_events[0]).UserId, ((LiveChatSubscriptionEvent)_events[2]).UserId);
    }

    [Test]
    public void AnonymousGiftsAndGiftBombs()
    {
        DebugChatCommands.Run(_chat, "/gift anon bob");
        DebugChatCommands.Run(_chat, "/giftbomb alice 3");

        Assert.IsTrue(((LiveChatGiftSubscriptionEvent)_events[0]).GifterIsAnonymous);
        Assert.AreEqual(3, ((LiveChatCommunityGiftEvent)_events[1]).Count);
        Assert.AreEqual(5, _events.Count, "One gift, then a community gift announcement and three gifted subs");
    }

    [Test]
    public void UnknownCommandsRaiseNothing()
    {
        DebugChatCommands.Run(_chat, "/nope");
        DebugChatCommands.Run(_chat, "/bits alice lots");

        Assert.AreEqual(0, _events.Count);
    }
}
