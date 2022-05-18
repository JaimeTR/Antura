using Antura.Database;
using Antura.Dog;
using Antura.Language;
using Antura.Profile;
using System;
using System.Collections;
using System.Collections.Generic;
using Antura.Helpers;
using Antura.Minigames;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Analytics;

namespace Antura.Core.Services.OnlineAnalytics
{
    public class Analytics : MonoBehaviour
    {

        /// <summary>
        ///
        /// TODO WIP: this methos saves the gameplay summary to remote/online analytics
        /// data is passed by the LogGamePlayData class
        ///
        /// 1 - Uuid: the unique player id
        /// 2 - app version(json app version + platform + device type (tablet/smartphone))
        /// 3 - player age(int) - player genre(string M/F)
        ///
        /// 4 - Journey Position(string Stage.LearningBlock.PlaySession)
        /// 5 - MiniGame(string code)
        ///
        /// - playtime(int seconds how long the gameplay)
        /// - launch type(from Journey or from Book)
        /// - end type(natural game end or forced exit)
        ///
        /// - difficulty(float from minigame config)
        /// - number of rounds(int from minigame config)
        /// - result(int 0,1,2,3 bones)
        ///
        /// - good answers(comma separated codes of vocabulary data)
        /// - wrong answers(comma separated codes of vocabulary data)
        /// - gameplay errors(say the lives in ColorTickle or anything not really related to Learning data)
        ///
        /// 10 - additional(json encoded additional parameters that we don't know now or custom specific per minigame)
        /// </summary>
        /// <param name="eventName">Event name.</param>
        ///

        public Analytics()
        {
        }

        async void Awake()
        {
            var options = new InitializationOptions();
            if (DebugConfig.I.DeveloperMode)
            {
                options.SetEnvironmentName("dev");
                Debug.LogWarning("Analytics in DEV environment");
            }
            await UnityServices.InitializeAsync(options);
        }

        private bool AnalyticsEnabled => AppManager.I.AppEdition.OnlineAnalyticsEnabled && AppManager.I.AppSettings.ShareAnalyticsEnabled;

        public void Init()
        {
            //  Debug.Log("init AnalyticsService");
        }

        private void AddSharedParameters(Dictionary<string, object> dict)
        {
            dict.Add("myPlayerUuid", AppManager.I.AppSettings.LastActivePlayerUUID.ToString());
            dict.Add("myEdition", AppManager.I.AppSettings.ContentID.ToString());
            dict.Add("myNativeLang", LanguageUtilities.GetISO3Code(AppManager.I.AppSettings.NativeLanguage));
        }

        public void TestEvent()
        {
            if (!AnalyticsEnabled)
                return;

            var parameters = new Dictionary<string, object>();
            AddSharedParameters(parameters);

            AnalyticsService.Instance.CustomData("myTestEvent", parameters);
            AnalyticsService.Instance.Flush();
            Debug.Log("Analytics TestEvent");
        }

        public void TrackCompletedRegistration(PlayerProfile playerProfile)
        {
            if (!AnalyticsEnabled)
                return;

            var avatarObJ = new Dictionary<string, object>()
            {
                { "id", playerProfile.AvatarId },
                { "bg_color", ColorUtility.ToHtmlStringRGB(playerProfile.BgColor) },
                { "hair_color", ColorUtility.ToHtmlStringRGB(playerProfile.HairColor) },
                { "skin_color", ColorUtility.ToHtmlStringRGB(playerProfile.SkinColor) },
                { "tint", playerProfile.Tint }
            };
            string myAvatar = JsonUtility.ToJson(avatarObJ);

            var parameters = new Dictionary<string, object>()
            {
                { "myAvatar", myAvatar },
                { "myGender", playerProfile.Gender.ToString() },
                { "myAge", playerProfile.Age }
            };
            AddSharedParameters(parameters);

            AnalyticsService.Instance.CustomData("myCompletedRegistration", parameters);
        }

        public void TrackReachedJourneyPosition(JourneyPosition jp)
        {
            if (!AnalyticsEnabled)
                return;

            var parameters = new Dictionary<string, object>()
            {
                { "myJP", jp.Id },
                { "myStage", jp.Stage },
                { "myLearningBlock", jp.LearningBlock },
                { "myPlaySession", jp.PlaySession }
            };
            AddSharedParameters(parameters);
            AnalyticsService.Instance.CustomData("myLevelUp", parameters);
        }

        public void TrackCompletedFirstContactPhase(FirstContactPhase phase)
        {
            if (!AnalyticsEnabled)
                return;

            //Dictionary<string, object> parameters = new Dictionary<string, object>()
            //{
            //    { "phase", (int)phase },
            //    { "phase_name", phase.ToString() }
            //};
            var parameters = new Dictionary<string, object>();
            AddSharedParameters(parameters);

            AnalyticsService.Instance.CustomData("myTutorialComplete", new Dictionary<string, object>());
        }

        public void TrackSpentBones(int nSpent, string boughtItemKey)
        {
            if (!AnalyticsEnabled)
                return;

            var parameters = new Dictionary<string, object>()
            {
                { "myBonesSpent", nSpent },
                { "myBoughtItem", boughtItemKey}
            };
            AddSharedParameters(parameters);
            AnalyticsService.Instance.CustomData("myItemBought", parameters);
        }

        public void TrackCustomization(AnturaCustomization customization, float anturaSpacePlayTime)
        {
            if (!AnalyticsEnabled)
                return;

            var parameters = new Dictionary<string, object>
            {
                { "myAnturaSpace_playtime", (int)anturaSpacePlayTime }
            };

            foreach (var pack in customization.PropPacks)
            {
                parameters.Add($"prop_{pack.Category}", pack.BaseId);
            }
            parameters.Add($"texture", customization.TexturePack.BaseId);
            parameters.Add($"decal", customization.DecalPack.BaseId);

            AddSharedParameters(parameters);
            AnalyticsService.Instance.CustomData("myAnturaCustomize", parameters);
        }

        public void TrackMiniGameScore(MiniGameCode miniGameCode, int score, JourneyPosition currentJourneyPosition, float duration)
        {
            if (!AnalyticsEnabled)
                return;

            var parameters = new Dictionary<string, object>()
            {
                { "myMinigame", miniGameCode.ToString() },
                { "myScore", score },
                { "myDuration", (int)duration },
                { "myJP", currentJourneyPosition.Id }
            };
            AddSharedParameters(parameters);
            AnalyticsService.Instance.CustomData("myMinigameEnd", parameters);
        }

        public void TrackVocabularyDataScore(MiniGameCode miniGameCode, JourneyPosition currentJourneyPosition, List<MinigamesLogManager.ILivingLetterAnswerData> answers)
        {
            if (!AnalyticsEnabled)
                return;

            foreach (var answer in answers)
            {
                if (answer._data == null) continue;
                var parameters = new Dictionary<string, object>()
                {
                    { "myMinigame", miniGameCode.ToString() },
                    { "myJP", currentJourneyPosition.Id },
                    { "myVocabularyDataType", answer._data.DataType },
                    { "myVocabularyDataId", answer._data.Id },
                    { "myVocabularyCorrect", answer._isPositiveResult },
                };
                AddSharedParameters(parameters);
                AnalyticsService.Instance.CustomData("myVocabularyDataScore", parameters);
            }
        }

        #region Older Events

        public void TrackKioskEvent(string eventName)
        {
            if (!AnalyticsEnabled)
                return;
            // var eventData = new Dictionary<string, object>{
            //         { "app", "kiosk" },
            //         {"lang", (AppManager.I.AppSettings.AppLanguage == AppLanguages.Italian ? "it" : "en")}
            //     };
            // Analytics.CustomEvent(eventName, eventData);
        }

        public void TrackGameEvent(LogGamePlayData _data)
        {
            if (!AnalyticsEnabled)
                return;
            // var eventName = "GamePlay";
            // var evetData = new Dictionary<string, object>{
            //     { "uuid", _data.Uuid },
            //     { "app", 2 },
            //     { "player", 3 }
            // }
            // Analytics.CustomEvent(eventName, evetData);

        }

        public void TrackScene(string sceneName)
        {
            if (!AnalyticsEnabled)
                return;
            //Analytics.CustomEvent("changeScene", new Dictionary<string, object> { { "scene", sceneName } });
        }

        public void TrackPlayerSession(int age, Profile.PlayerGender gender)
        {
            if (!AnalyticsEnabled)
                return;
            //Gender playerGender = (gender == Profile.PlayerGender.F ? Gender.Female : Gender.Male);
            //Analytics.SetUserGender(playerGender);
            //int birthYear = DateTime.Now.Year - age;
            //Analytics.SetUserBirthYear(birthYear);
        }

        #endregion
    }
}
