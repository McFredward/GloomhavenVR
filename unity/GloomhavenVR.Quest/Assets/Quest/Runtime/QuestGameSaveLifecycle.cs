#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Requests the native host/local save flow when Android suspends the app.</summary>
    public sealed class QuestGameSaveLifecycle : MonoBehaviour
    {
        private static bool installed;
        private bool paused, pending;
        private int request;

        internal static void Install()
        {
            if (!QuestStandalonePlatform.Enabled || installed) return;
            var host = new GameObject("Quest local save lifecycle");
            DontDestroyOnLoad(host);
            host.AddComponent<QuestGameSaveLifecycle>();
            installed = true;
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (!QuestStandalonePlatform.Enabled || paused == isPaused) return;
            paused = isPaused;
            if (!isPaused)
            {
                if (pending)
                    Debug.LogWarning("[Quest saves] Resume while native pause-save request remains pending; original callbacks may need resumed frames. No completed write is asserted.");
                return;
            }
            if (pending) return;
            SaveData save = SaveData.Instance;
            if (save == null || save.Global == null || save.GameBootedForAutoTests) return;
            GlobalData global = save.Global;
            PartyAdventureData party = global.CurrentAdventureData;
            bool adventure = global.GameMode == EGameMode.Campaign && party != null && !FFSNetwork.IsClient
                && ((FFSNetwork.IsOnline && FFSNetwork.IsHost) || IsLocalOwner(party.Owner));
            pending = true;
            int generation = ++request;
            Debug.Log("[Quest saves] Pause-save requested through original queue; request=" + generation
                + "; adventure=" + adventure + "; queueAlreadyExecuting=" + save.SaveQueue.IsAnyOperationExecuting
                + ". Android suspension may interrupt completion; no immediate flush is guaranteed.");
            try
            {
                if (adventure)
                    save.SaveCurrentAdventureData(() => QueueGlobal(save, global, party, generation));
                else QueueGlobal(save, global, null, generation);
            }
            catch (Exception error)
            {
                pending = false;
                Debug.LogError("[Quest saves] Native pause-save request failed: " + error);
            }
        }

        // Exact PC/Android admission rule from SaveData.NeedToCreateSave. Keep
        // imported owner bytes intact; the original load flow offers local copies.
        internal static bool IsLocalOwner(SaveOwner owner)
        {
            if (owner == null || PlatformLayer.UserData == null) return false;
            string network = owner.PlatformNetworkAccountID;
            return (!string.IsNullOrEmpty(network) && network != "0"
                    && network == PlatformLayer.UserData.PlatformNetworkAccountPlayerID)
                || owner.PlatformAccountID == PlatformLayer.UserData.PlatformAccountID;
        }

        private void QueueGlobal(SaveData save, GlobalData global, PartyAdventureData party, int generation)
        {
            if (generation != request || !pending) return;
            if (SaveData.Instance != save || save.Global != global
                || (party != null && global.CurrentAdventureData != party))
            {
                pending = false;
                Debug.LogWarning("[Quest saves] Pause-save callback belongs to a replaced native save context; no new context was written.");
                return;
            }
            try
            {
                save.SaveGlobalData();
                // A native queue barrier can execute only after the previous
                // original operations invoke their callbacks. It cannot certify
                // their success: native unsupported map phases/retry failures
                // can also invoke completion. Do not alter those semantics.
                save.PerformCustomFileOperation(done =>
                {
                    if (generation == request)
                    {
                        pending = false;
                        Debug.Log("[Quest saves] Native pause-save queue barrier reached; request=" + generation
                            + ". Callback sequence completed; native errors/unsupported phases remain authoritative, not a durability guarantee.");
                    }
                    done();
                });
            }
            catch (Exception error)
            {
                pending = false;
                Debug.LogError("[Quest saves] Native pause-save global/queue request failed: " + error);
            }
        }
    }
}
#endif
