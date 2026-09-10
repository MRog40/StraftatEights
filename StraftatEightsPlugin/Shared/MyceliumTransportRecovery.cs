using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class MyceliumTransportRecovery
{
    private const int Channel = 120;
    private const int AutoRestartBrokenSession = 0x20;
    private const int MaxQueuedMessages = 128;
    private const int MaxAttempts = 5;
    private static readonly float[] RetryDelays = { 0.35f, 0.75f, 1.5f, 3f, 6f };
    private static readonly List<PendingMessage> PendingMessages = new();
    private static readonly Dictionary<ulong, ProbeState> Probes = new();
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

        SteamNetworkingIdentity identity = default;
        identity.SetSteamID(remote);
        SteamNetworkingMessages.CloseSessionWithUser(ref identity);
        QueueProbe(remote, callback.m_info.m_eState.ToString());
    }

    internal static void OnProbe(CSteamID sender, int probeId)
    {
        if (!MyceliumNetwork.InLobby || sender.m_SteamID == 0 || !MyceliumNetwork.Players.Contains(sender))
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.MyceliumTransportModId,
            nameof(Plugin.MyceliumSessionProbeAck), sender, ReliableType.Reliable, probeId);
    }

    internal static void OnProbeAck(CSteamID sender, int probeId)
    {
        if (sender.m_SteamID == 0 || !Probes.TryGetValue(sender.m_SteamID, out ProbeState? probe)
            || probe.ProbeId != probeId)
        {
            return;
        }

        Probes.Remove(sender.m_SteamID);
        DebugLog.Info($"Mycelium session probe acknowledged peer={sender.m_SteamID} probe={probeId}");
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
            Enqueue(data, target, reliable, exception.GetBaseException().Message);
            return true;
        }
        if (result == EResult.k_EResultOK)
        {
            return true;
        }

        Enqueue(data, target, reliable, result.ToString());
        return true;
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
                result = EResult.k_EResultNoConnection;
                DebugLog.Info($"Mycelium retry exception peer={pending.Target.m_SteamID} "
                    + $"attempt={pending.Attempt} error={exception.GetBaseException().Message}");
            }
            if (result == EResult.k_EResultOK)
            {
                PendingMessages.RemoveAt(index);
                DebugLog.Info($"Mycelium retry delivered peer={pending.Target.m_SteamID} "
                    + $"attempt={pending.Attempt}");
                continue;
            }

            pending.Attempt++;
            if (pending.Attempt >= MaxAttempts)
            {
                PendingMessages.RemoveAt(index);
                DebugLog.Info($"Mycelium retry abandoned peer={pending.Target.m_SteamID} "
                    + $"attempts={pending.Attempt} result={result}");
                continue;
            }

            pending.NextAttempt = now + RetryDelays[pending.Attempt - 1];
        }
    }

    private static void ProcessProbes()
    {
        float now = Time.unscaledTime;
        foreach (KeyValuePair<ulong, ProbeState> entry in Probes.ToArray())
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
                DebugLog.Info($"Mycelium session probe abandoned peer={entry.Key} attempts={probe.Attempt}");
                continue;
            }

            probe.Attempt++;
            probe.NextAttempt = now + RetryDelays[Math.Min(probe.Attempt - 1, RetryDelays.Length - 1)];
            MyceliumNetwork.RPCTarget(Plugin.MyceliumTransportModId,
                nameof(Plugin.MyceliumSessionProbe), entry.Value.Target,
                ReliableType.Reliable, probe.ProbeId);
        }
    }

    private static void QueueProbe(CSteamID target, string reason)
    {
        if (Probes.TryGetValue(target.m_SteamID, out ProbeState? existing))
        {
            existing.NextAttempt = Math.Min(existing.NextAttempt, Time.unscaledTime + 0.25f);
            DebugLog.Info($"Mycelium session reset peer={target.m_SteamID} reason={reason} "
                + $"probe={existing.ProbeId}");
            return;
        }

        int probeId = ++_nextProbeId;
        Probes[target.m_SteamID] = new ProbeState(target, probeId, Time.unscaledTime + 0.25f);
        DebugLog.Info($"Mycelium session reset peer={target.m_SteamID} reason={reason} probe={probeId}");
    }

    private static void Enqueue(byte[] data, CSteamID target, ReliableType reliable, string reason)
    {
        if (PendingMessages.Any(pending => pending.Target == target
            && pending.Reliable == reliable && pending.Data.SequenceEqual(data)))
        {
            return;
        }

        while (PendingMessages.Count >= MaxQueuedMessages)
        {
            PendingMessages.RemoveAt(0);
        }

        PendingMessages.Add(new PendingMessage(data.ToArray(), target, reliable,
            Time.unscaledTime + RetryDelays[0]));
        DebugLog.Info($"Mycelium send queued peer={target.m_SteamID} result={reason} "
            + $"pending={PendingMessages.Count}");
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
    }

    private sealed class PendingMessage
    {
        internal readonly byte[] Data;
        internal readonly CSteamID Target;
        internal readonly ReliableType Reliable;
        internal int Attempt = 1;
        internal float NextAttempt;

        internal PendingMessage(byte[] data, CSteamID target, ReliableType reliable, float nextAttempt)
        {
            Data = data;
            Target = target;
            Reliable = reliable;
            NextAttempt = nextAttempt;
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