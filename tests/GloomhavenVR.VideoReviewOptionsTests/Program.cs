using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Main(string[] args)
    {
        string[] keys = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[0]))!;
        NativeVideoReview.Keys = keys;
        Check(keys.Length == 20 && keys.Distinct().Count() == keys.Length,
            "Every audited movie has exactly one canonical action");
        Check(!VROptionsTab.Cheats && ModuleConfig.LastModule == "cheats",
            "Actual Cheats config and source-bound default start disabled");
        var englishNames = new Dictionary<string, string>();
        foreach (string language in new[] { "English", "German", "French" })
        {
            Loc.CurrentLanguage = language;
            NativeVideoReview.Reset();
            VROptionsTab.IsOpen = true;
            VROptionsTab.Cheats = false;
            VROptionsTab.Build();
            Check(!VROptionsTab.Headers.Contains(Loc.VideoReviewHeading) && VROptionsTab.Actions.Count == 0,
                "Disabled Cheats page falls back to the index without spoiler names");
            Check(VROptionsTab.BuildCheatsDoor() == 0 && VROptionsTab.Actions.Count == 0,
                "Disabled Cheats has no Advanced index door");
            Check(VROptionsTab.BuildDirectReview() == 0 && VROptionsTab.Actions.Count == 0,
                "Direct video builder cannot reveal a catalogue with Cheats disabled");
            VROptionsTab.Cheats = true;
            VROptionsTab.BuildPublic();
            Check(!VROptionsTab.Headers.Contains(Loc.VideoReviewHeading)
                && !VROptionsTab.Actions.Any(row => keys.Any(key => row.Caption == Loc.VideoReviewPlay(key)))
                && !VROptionsTab.Notes.Contains(Loc.VideoReviewHint) && VROptionsTab.Caption == "",
                "Public test triggers never expose spoiler movie actions");
            Check(VROptionsTab.BuildCheatsDoor() == 1, "Enabled Cheats has its gated Advanced index door");
            FFSNetwork.IsOnline = true;
            VROptionsTab.Build();
            Check(VROptionsTab.Actions.Count == keys.Length + 7,
                "All audited movies, back, close and progress actions remain reachable");
            Check(VROptionsTab.Headers.Contains(Loc.VideoReviewHeading), "Video block is on the gated Cheats page");
            Check(VROptionsTab.SaveRowOnline.Count == 5 && VROptionsTab.SaveRowOnline.All(online => online),
                "Online refusal remains a per-save-action boundary, not a video page restriction");
            Check(VROptionsTab.Notes.Contains(Loc.VideoReviewCheatsScope), "Cheats scope distinguishes progress changes from shared video previews");
            Check(VROptionsTab.ReservedLines >= 3, "Live status reserves localized feedback space");
            Check(VROptionsTab.Notes.Contains(Loc.VideoReviewHint), "Review explains ordinary window and unchanged progress");
            Check(VROptionsTab.Caption == Loc.VideoReviewReady, "Initial review state is visible");
            foreach (string key in keys)
            {
                string name = Loc.VideoReviewName(key);
                Check(name != "Video" && !name.Contains(".mov") && !name.Contains('/'),
                    "Every movie has a player name without file details");
                if (language == "English") englishNames.Add(key, name);
                if (language == "German" && !key.StartsWith("Embedded/"))
                    Check(name != englishNames[key], "German movie names are localized");
                if (language == "French")
                    Check(name == englishNames[key], "Unsupported language has English movie fallback");
                string caption = Loc.VideoReviewPlay(key);
                Check(VROptionsTab.Actions.Count(row => row.Caption == caption) == 1,
                    "Each named movie has exactly one action");
                var action = VROptionsTab.Actions.Single(row => row.Caption == caption);
                Check(action.IsAction, "Movie rows are one-shot actions, not menu toggles");
                NativeVideoReview.Stop();
                VROptionsTab.Refresh();
                int oldRows = VROptionsTab.Actions.Count;
                int oldStarts = NativeVideoReview.Starts;
                action.Click();
                Check(NativeVideoReview.Requests.LastOrDefault() == key, "Each movie requests its own exact canonical key");
                Check(NativeVideoReview.Starts == oldStarts + 1 && NativeVideoReview.Active,
                    "Closed movie can be deliberately replayed");
                Check(VROptionsTab.Caption == Loc.VideoReviewPreparing(name), "Preparation feedback appears on the press");
                Check(VROptionsTab.Actions.Count == oldRows && VROptionsTab.Rebuilds == 0,
                    "Movie press keeps the existing controls under the pointer");
                NativeVideoReview.Status = NativeVideoReviewStatus.Playing;
                VROptionsTab.Refresh();
                Check(VROptionsTab.Caption == Loc.VideoReviewPlaying(name), "Asynchronous playing result is visible");
                int writes = VROptionsTab.StatusLabel!.Writes;
                for (int frame = 0; frame < 64; frame++) VROptionsTab.Refresh();
                Check(VROptionsTab.StatusLabel.Writes == writes, "Unchanged state does not repaint the label");
                action.Click();
                Check(NativeVideoReview.Starts == oldStarts + 1 && NativeVideoReview.Active,
                    "Busy press leaves the current review intact");
                Check(VROptionsTab.Caption == Loc.VideoReviewAlreadyPlaying, "Busy feedback appears on the press");
                VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewClose).Click();
                Check(!NativeVideoReview.Active && VROptionsTab.Caption == Loc.VideoReviewClosed,
                    "Explicit close releases the review and explains replay");
            }
            var staleMovies = VROptionsTab.Actions.Where(row => keys.Any(key => row.Caption == Loc.VideoReviewPlay(key))).ToArray();
            var staleClose = VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewClose);
            NativeVideoReview.Active = true;
            NativeVideoReview.Status = NativeVideoReviewStatus.Playing;
            NativeVideoReview.LastKey = keys[0];
            VROptionsTab.Refresh();
            int requests = NativeVideoReview.Requests.Count;
            VROptionsTab.Cheats = false;
            VROptionsTab.Refresh();
            Check(staleMovies.All(row => !row.Row.activeSelf), "Revoked Cheats immediately removes spoiler movie rows");
            Check(VROptionsTab.Caption == Loc.VideoReviewCheatsRequired, "Revoked Cheats feedback contains no movie name");
            foreach (var stale in staleMovies) stale.Click();
            Check(NativeVideoReview.Requests.Count == requests && NativeVideoReview.Active,
                "Revoked Cheats callbacks never call TryStart");
            staleClose.Click();
            Check(!NativeVideoReview.Active, "Revoked Cheats still permits closing an existing review");
            VROptionsTab.Cheats = true;
            VROptionsTab.Refresh();
            Check(staleMovies.All(row => row.Row.activeSelf) && VROptionsTab.Caption == Loc.VideoReviewClosed,
                "Reenabled Cheats restores the same rows and current status without rebuilding");
            NativeVideoReview.Context = false;
            int starts = NativeVideoReview.Starts;
            VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewPlay(keys[0])).Click();
            Check(NativeVideoReview.Starts == starts && !NativeVideoReview.Active,
                "Invalid scenario refusal does not start or queue a review");
            Check(VROptionsTab.Caption == Loc.VideoReviewScenarioRequired,
                "Invalid context explains that a scenario is required");
            foreach (var pair in new[]
            {
                (NativeVideoReviewFailure.NativeMovieActive, Loc.VideoReviewNativeBusy),
                (NativeVideoReviewFailure.RemoteMovieActive, Loc.VideoReviewNativeBusy),
                (NativeVideoReviewFailure.MediaUnavailable, Loc.VideoReviewUnavailable),
                (NativeVideoReviewFailure.InvalidMovie, Loc.VideoReviewFailed),
                (NativeVideoReviewFailure.DecoderError, Loc.VideoReviewFailed),
                (NativeVideoReviewFailure.None, Loc.VideoReviewFailed),
            })
            {
                NativeVideoReview.Status = NativeVideoReviewStatus.Failed;
                NativeVideoReview.LastFailure = pair.Item1;
                VROptionsTab.Refresh();
                Check(VROptionsTab.Caption == pair.Item2, "Failure feedback identifies the next useful action");
                Check(!VROptionsTab.Caption.Contains(".mov") && !VROptionsTab.Caption.Contains("Decoder"),
                    "Failure feedback uses player language");
            }
            NativeVideoReview.Context = true;
            VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewPlay(keys[0])).Click();
            Check(NativeVideoReview.Active && VROptionsTab.Caption == Loc.VideoReviewPreparing(Loc.VideoReviewName(keys[0])),
                "A failed attempt can be retried without restarting");
            int stops = NativeVideoReview.Stops;
            var oldLabel = VROptionsTab.StatusLabel!;
            string visible = oldLabel.text;
            VROptionsTab.IsOpen = false;
            NativeVideoReview.Status = NativeVideoReviewStatus.Playing;
            VROptionsTab.Refresh();
            Check(oldLabel.text == visible && NativeVideoReview.Stops == stops && NativeVideoReview.Active,
                "Closing options leaves playback intact and does not repaint hidden controls");
            VROptionsTab.IsOpen = true;
            VROptionsTab.Refresh();
            Check(VROptionsTab.Caption == Loc.VideoReviewPlaying(Loc.VideoReviewName(keys[0])),
                "Reopened options catch up with current playback");
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 512; frame++) VROptionsTab.Refresh();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(allocated == 0, "Unchanged open status does not allocate per frame");
            VROptionsTab.Clear();
            oldLabel.Destroyed = true;
            NativeVideoReview.Status = NativeVideoReviewStatus.Closed;
            VROptionsTab.Refresh();
            Check(oldLabel.text != Loc.VideoReviewClosed && NativeVideoReview.Stops == stops,
                "Clearing UI references cannot repaint old labels or stop a video");
            VROptionsTab.Build();
            Check(VROptionsTab.Caption == Loc.VideoReviewClosed, "Rebuilt pane shows the current closed result");
            NativeVideoReview.Active = true;
            NativeVideoReview.Status = NativeVideoReviewStatus.Preparing;
            ElementMood.Forcing = Haunt.Forcing = true;
            int presses = GloomhavenVR.Net.RemoteTestTriggers.Presses;
            VROptionsTab.BuildPublic();
            VROptionsTab.Actions.Single(row => row.Caption == "vr_tt_stop").Click();
            Check(!NativeVideoReview.Active && NativeVideoReview.Stops == stops + 1,
                "All-off closes the owned review movie");
            Check(!ElementMood.Forcing && !Haunt.Forcing && GloomhavenVR.Net.RemoteTestTriggers.Presses == presses + 1,
                "All-off preserves both existing effect releases and their shared press");
            Check(NativeVideoReview.Status == NativeVideoReviewStatus.Closed && VROptionsTab.Caption == "",
                "Public all-off closes playback without exposing a spoiler status");

            // An observer has no local review decoder for lifecycle Stop() to close.
            // Both GUI doors must request shared closure through the explicit API.
            NativeVideoReview.Reset();
            NativeVideoReview.ObservingReview = true;
            NativeVideoReview.Status = NativeVideoReviewStatus.Playing;
            VROptionsTab.Build();
            VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewClose).Click();
            Check(NativeVideoReview.CloseRequests == 1 && NativeVideoReview.ObserverCloseRequests == 1,
                "Observer close sends shared review close intent through the GUI API");
            Check(NativeVideoReview.Stops == 0 && NativeVideoReview.Starts == 0 && !NativeVideoReview.Active,
                "Observer close does not borrow or create a local decoder");
            Check(VROptionsTab.Caption == Loc.VideoReviewClosed && VROptionsTab.Rebuilds == 0,
                "Observer close shows API feedback without rebuilding controls");
            NativeVideoReview.Reset();
            NativeVideoReview.ObservingReview = true;
            NativeVideoReview.Status = NativeVideoReviewStatus.Preparing;
            ElementMood.Forcing = Haunt.Forcing = true;
            presses = GloomhavenVR.Net.RemoteTestTriggers.Presses;
            VROptionsTab.BuildPublic();
            VROptionsTab.Actions.Single(row => row.Caption == "vr_tt_stop").Click();
            Check(NativeVideoReview.CloseRequests == 1 && NativeVideoReview.ObserverCloseRequests == 1,
                "Observer all-off sends shared review close intent through the GUI API");
            Check(NativeVideoReview.Stops == 0 && !ElementMood.Forcing && !Haunt.Forcing
                  && GloomhavenVR.Net.RemoteTestTriggers.Presses == presses + 1,
                "Observer all-off preserves both existing shared effect releases");
            Check(NativeVideoReview.Status == NativeVideoReviewStatus.Closed && VROptionsTab.Rebuilds == 0,
                "Observer all-off shows API feedback without rebuilding controls");
            NativeVideoReview.Reset();
            NativeVideoReview.ObservingNativeMovie = true;
            NativeVideoReview.Status = NativeVideoReviewStatus.Busy;
            NativeVideoReview.LastFailure = NativeVideoReviewFailure.RemoteMovieActive;
            VROptionsTab.Build();
            foreach (string key in keys)
                VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewPlay(key)).Click();
            Check(NativeVideoReview.Requests.Count == keys.Length && NativeVideoReview.Starts == 0
                && NativeVideoReview.ObservingNativeMovie && NativeVideoReview.LastFailure == NativeVideoReviewFailure.RemoteMovieActive,
                "Enabled Cheats still delegates every movie to native-priority admission");
            VROptionsTab.Actions.Single(row => row.Caption == Loc.VideoReviewClose).Click();
            VROptionsTab.BuildPublic();
            VROptionsTab.Actions.Single(row => row.Caption == "vr_tt_stop").Click();
            Check(NativeVideoReview.CloseRequests == 2 && NativeVideoReview.ObserverCloseRequests == 0
                  && NativeVideoReview.Stops == 0 && NativeVideoReview.ObservingNativeMovie,
                "GUI close requests cannot close a genuine peer game movie");
            VROptionsTab.Build();
            Check(VROptionsTab.Caption == Loc.VideoReviewNativeBusy,
                "Refused native close retains the existing localized game-video feedback");
            VROptionsTab.Build(noRoot: true);
            Check(VROptionsTab.Actions.Count == 0, "No menu root means no abandoned movie actions");
        }
        Loc.CurrentLanguage = "German";
        Check(Loc.VideoReviewScenarioRequired.Contains("Szenario") &&
              Loc.VideoReviewNativeBusy.Contains("Spielvideo") && Loc.VideoReviewClosed.Contains("geschlossen"),
            "German lifecycle feedback uses German player language");
        ScenarioRuleLibrary.CharacterClassManager.Classes.Add(new()
        {
            CharacterModel = ScenarioRuleLibrary.ECharacter.Brute, LocKey = "NATIVE_CLASS_OTHER",
        });
        ScenarioRuleLibrary.CharacterClassManager.Classes.Add(new()
        {
            CharacterModel = ScenarioRuleLibrary.ECharacter.BeastTyrant, LocKey = "NATIVE_CLASS_BEAST_TYRANT",
        });
        foreach (string language in new[] { "English", "German", "French" })
        {
            Loc.CurrentLanguage = language;
            Check(Loc.VideoReviewName("Heroes/BeastTyrant.mov") == "Native " + language,
                "Hero title uses its own original class localization without an unlock gate");
        }
        Console.WriteLine($"PASS video review options: {_assertions} assertions; 20 gated movie actions in multiplayer, public spoiler exclusion, EN/DE and fallback, lifecycle and stop-all.");
    }
}
