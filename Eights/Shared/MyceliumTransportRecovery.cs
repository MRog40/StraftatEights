using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class MyceliumTransportRecovery
{
    private const int Channel = 120;
    private const int AutoRestartBrokenSession = 0x20;
    private const int MaxQueuedMessages = 128;
    private const int MaxAttempts = 5;
    private static readonly float[] RetryDelays = { 0.35f, 0.75f, 1.5f, 3f, 6f };
    private static readonly List<PendingMessage> PendingMessages = new();
    private static readonly Dictionary<ulong, ProbeState> Probes = new();
    private static readonly List<KeyValuePair<ulong, ProbeState>> ProbeEntries = new();
    private static readonly Dictionary<ulong, int> ProbeSendDepth = new();
    private static readonly Dictionary<ulong, float> NextSessionCloseTimes = new();
    private static int _nextProbeId;

    internal static void Initialize()
    {
        MyceliumNetwork.RegisterNetworkObject(Plugin.Instance, Plugin.MyceliumTransportModId);
        MyceliumNetwork.LobbyLeft += Reset;
    }

    internal static void Update()
    {
        if (!MyceliumNetwork.InLobby)
        {
            Reset();
            return;
        }

        ProcessPendingMessages();
        ProcessProbes();
    }

    internal static void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t callback)
    {
        if (!MyceliumNetwork.InLobby)
        {
            return;
        }

        CSteamID remote = callback.m_info.m_identityRemote.GetSteamID();
        if (remote.m_SteamID == 0 || remote == SteamUser.GetSteamID()
            || !MyceliumNetwork.Players.Contains(remote))
        {
            return;
        }

        if (!NextSessionCloseTimes.TryGetValue(remote.m_SteamID, out float nextClose)
            || Time.unscaledTime >= nextClose)
        {
            SteamNetworkingIdentity identity = default;
            identity.SetSteamID(remote);
            SteamNetworkingMessages.CloseSessionWithUser(ref identity);
            NextSessionCloseTimes[remote.m_SteamID] = Time.unscaledTime + 1.5f;
        }

        QueueProbe(remote, callback.m_info.m_eState.ToString());
    }

    internal static void OnProbe(CSteamID sender, int probeId)
    {
        if (!MyceliumNetwork.InLobby || sender.m_SteamID == 0 || !MyceliumNetwork.Players.Contains(sender))
        {
            return;
        }

        SendProbeRpc(sender, nameof(Plugin.MyceliumSessionProbeAck), probeId);
    }

    internal static void OnProbeAck(CSteamID sender, int probeId)
    {
        if (sender.m_SteamID == 0 || !Probes.TryGetValue(sender.m_SteamID, out ProbeState? probe)
            || probe.ProbeId != probeId)
        {
            return;
        }

        Probes.Remove(sender.m_SteamID);
        NextSessionCloseTimes.Remove(sender.m_SteamID);
    }

    internal static bool TrySend(byte[] data, CSteamID target, ReliableType reliable)
    {
        if (data.Length > 32768 || target.m_SteamID == 0 || target == SteamUser.GetSteamID())
        {
            return false;
        }

        EResult result;
        try
        {
            result = SendRaw(data, target, reliable);
        }
        catch (Exception exception)
        {
            if (IsProbeSend(target))
            {
            }
            else
            {
                Enqueue(data, target, reliable, exception.GetBaseException().Message);
            }
            return true;
        }
        if (result == EResult.k_EResultOK)
        {
            return true;
        }

        if (IsProbeSend(target))
        {
        }
        else
        {
            Enqueue(data, target, reliable, result.ToString());
        }
        return true;
    }

    private static bool IsProbeSend(CSteamID target)
    {
        return ProbeSendDepth.ContainsKey(target.m_SteamID);
    }

    private static void ProcessPendingMessages()
    {
        float now = Time.unscaledTime;
        for (int index = PendingMessages.Count - 1; index >= 0; index--)
        {
            PendingMessage pending = PendingMessages[index];
            if (now < pending.NextAttempt)
            {
                continue;
            }

            EResult result;
            try
            {
                result = SendRaw(pending.Data, pending.Target, pending.Reliable);
            }
            catch (Exception exception)
            {
                pending.LastFailureReason = exception.GetBaseException().Message;
                result = EResult.k_EResultNoConnection;
            }
            if (result != EResult.k_EResultOK && pending.LastFailureReason.Length == 0)
            {
                pending.LastFailureReason = result.ToString();
            }
            if (result == EResult.k_EResultOK)
            {
                PendingMessages.RemoveAt(index);
                continue;
            }

            pending.Attempt++;
            if (pending.Attempt >= MaxAttempts)
            {
                Plugin.Logger.LogWarning($"[Mycelium] Dropped queued message after "
                    + $"{pending.Attempt} attempts to {pending.Target.m_SteamID}: "
                    + pending.LastFailureReason);
                PendingMessages.RemoveAt(index);
                continue;
            }

            pending.NextAttempt = now + RetryDelays[pending.Attempt - 1];
        }
    }

    private static void ProcessProbes()
    {
        float now = Time.unscaledTime;
        ProbeEntries.Clear();
        foreach (KeyValuePair<ulong, ProbeState> entry in Probes)
        {
            ProbeEntries.Add(entry);
        }

        foreach (KeyValuePair<ulong, ProbeState> entry in ProbeEntries)
        {
            ProbeState probe = entry.Value;
            if (now < probe.NextAttempt)
            {
                continue;
            }

            if (!MyceliumNetwork.Players.Any(player => player.m_SteamID == entry.Key))
            {
                Probes.Remove(entry.Key);
                continue;
            }

            if (probe.Attempt >= MaxAttempts)
            {
                Probes.Remove(entry.Key);
                continue;
            }

            probe.Attempt++;
            probe.NextAttempt = now + RetryDelays[Math.Min(probe.Attempt - 1, RetryDelays.Length - 1)];
            SendProbeRpc(probe.Target, nameof(Plugin.MyceliumSessionProbe), probe.ProbeId);
        }
    }

    private static void QueueProbe(CSteamID target, string reason)
    {
        if (Probes.TryGetValue(target.m_SteamID, out ProbeState? existing))
        {
            return;
        }

        int probeId = ++_nextProbeId;
        Probes[target.m_SteamID] = new ProbeState(target, probeId, Time.unscaledTime + RetryDelays[1]);
    }

    private static void SendProbeRpc(CSteamID target, string methodName, int probeId)
    {
        ulong steamId = target.m_SteamID;
        ProbeSendDepth.TryGetValue(steamId, out int depth);
        ProbeSendDepth[steamId] = depth + 1;
        try
        {
            MyceliumNetwork.RPCTarget(Plugin.MyceliumTransportModId, methodName, target,
                ReliableType.Reliable, probeId);
        }
        finally
        {
            if (depth == 0)
            {
                ProbeSendDepth.Remove(steamId);
            }
            else
            {
                ProbeSendDepth[steamId] = depth;
            }
        }
    }

    private static void Enqueue(byte[] data, CSteamID target, ReliableType reliable, string reason)
    {
        PendingMessage? existing = PendingMessages.FirstOrDefault(pending => pending.Target == target
            && pending.Reliable == reliable && pending.Data.SequenceEqual(data));
        if (existing != null)
        {
            existing.LastFailureReason = reason;
            return;
        }

        while (PendingMessages.Count >= MaxQueuedMessages)
        {
            PendingMessages.RemoveAt(0);
        }

        PendingMessages.Add(new PendingMessage(data.ToArray(), target, reliable,
            Time.unscaledTime + RetryDelays[0], reason));
    }

    private static EResult SendRaw(byte[] data, CSteamID target, ReliableType reliable)
    {
        SteamNetworkingIdentity identity = default;
        identity.SetSteamID(target);
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            IntPtr pointer = handle.AddrOfPinnedObject();
            int flags = reliable switch
            {
                ReliableType.Reliable => 8,
                ReliableType.UnreliableNoDelay => 5,
                _ => 0
            } | AutoRestartBrokenSession;
            return SteamNetworkingMessages.SendMessageToUser(ref identity, pointer,
                (uint)data.Length, flags, Channel);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void Reset()
    {
        PendingMessages.Clear();
        Probes.Clear();
        ProbeSendDepth.Clear();
        NextSessionCloseTimes.Clear();
    }

    private sealed class PendingMessage
    {
        internal readonly byte[] Data;
        internal readonly CSteamID Target;
        internal readonly ReliableType Reliable;
        internal int Attempt = 1;
        internal float NextAttempt;
        internal string LastFailureReason { get; set; }

        internal PendingMessage(byte[] data, CSteamID target, ReliableType reliable,
            float nextAttempt, string failureReason)
        {
            Data = data;
            Target = target;
            Reliable = reliable;
            NextAttempt = nextAttempt;
            LastFailureReason = failureReason;
        }
    }

    private sealed class ProbeState
    {
        internal readonly CSteamID Target;
        internal readonly int ProbeId;
        internal int Attempt;
        internal float NextAttempt;

        internal ProbeState(CSteamID target, int probeId, float nextAttempt)
        {
            Target = target;
            ProbeId = probeId;
            NextAttempt = nextAttempt;
        }
    }
}

[HarmonyPatch(typeof(MyceliumNetwork), "SendBytes")]
internal static class MyceliumNetwork_SendBytes_Patch
{
    private static bool Prefix(byte[] data, CSteamID target, ReliableType reliable)
    {
        return !MyceliumTransportRecovery.TrySend(data, target, reliable);
    }
}

[HarmonyPatch(typeof(MyceliumNetwork), "OnSessionRequestFailed")]
internal static class MyceliumNetwork_SessionFailed_Patch
{
    private static void Postfix(SteamNetworkingMessagesSessionFailed_t param)
    {
        MyceliumTransportRecovery.OnSessionFailed(param);
    }
}

public partial class Plugin
{
    internal const uint MyceliumTransportModId = 1618033993u;

    [CustomRPC]
    public void MyceliumSessionProbe(int probeId, RPCInfo info)
    {
        MyceliumTransportRecovery.OnProbe(info.SenderSteamID, probeId);
    }

    [CustomRPC]
    public void MyceliumSessionProbeAck(int probeId, RPCInfo info)
    {
        MyceliumTransportRecovery.OnProbeAck(info.SenderSteamID, probeId);
    }
}