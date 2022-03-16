using Antura.Database;
using System;
using System.Collections;
using System.Collections.Generic;
using Antura.Dog;
using Antura.Profile;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Analytics;

namespace Antura.Core.Services.OnlineAnalytics
{
    public class AnalyticsService
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

        public AnalyticsService()
        {
        }

        async void Awake()
        {
            var options = new InitializationOptions();
            options.SetEnvironmentName("dev");
            UnityServices.InitializeAsync(options);
        }

        private bool AnalyticsEnabled
        {
            get
            {
                return AppManager.I.AppEdition.OnlineAnalyticsEnabled && AppManager.I.AppSettings.ShareAnalyticsEnabled;
            }
        }

        public void TrackCompletedRegistration(PlayerProfile playerProfile)
        {
            if (!AnalyticsEnabled)
                return;

            //var parameters = new Dictionary<string, object>();
            //parameters["avatar_id"] = playerProfile.AvatarId;
            //parameters["avatar_bg_color"] = ColorUtility.ToHtmlStringRGB(playerProfile.BgColor);
            //parameters["avatar_hair_color"] = ColorUtility.ToHtmlStringRGB(playerProfile.HairColor);
            //parameters["avatar_skin_color"] = ColorUtility.ToHtmlStringRGB(playerProfile.SkinColor);
            //parameters["avatar_tint"] = playerProfile.Tint;
            //parameters["gender"] = playerProfile.Gender;
            //parameters["age"] = playerProfile.Age;
            //Analytics.CustomEvent("CompletedRegistration", parameters);

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

            Events.CustomData("myCompletedRegistration", parameters);

#if FB_SDK
            AppManager.I.FacebookManager.LogAppEvent(Facebook.Unity.AppEventName.CompletedRegistration, parameters: parameters);
#endif
        }

        public void TrackReachedJourneyPosition(JourneyPosition jp)
        {
            if (!AnalyticsEnabled)
                return;

            //var parameters = new Dictionary<string, object>();
            //parameters["jp"] = jp.Id;
            //parameters["st"] = jp.Stage;
            //parameters["lb"] = jp.LearningBlock;
            //parameters["ps"] = jp.PlaySession;

            ////Analytics.CustomEvent("AchievedLevel", parameters);
            //AnalyticsEvent.LevelUp(jp.Id, parameters);

            var parameters = new Dictionary<string, object>()
            {
                { "JP", jp.Id },
                { "Stage", jp.Stage },
                { "LearningBlock", jp.LearningBlock },
                { "PlaySession", jp.PlaySession }
            };

            Events.CustomData("myLevelUp", parameters);
#if FB_SDK
            AppManager.I.FacebookManager.LogAppEvent(Facebook.Unity.AppEventName.AchievedLevel, parameters: parameters);
#endif
        }

        public void TrackCompletedFirstContactPhase(FirstContactPhase phase)
        {
            if (!AnalyticsEnabled)
                return;

            //var parameters = new Dictionary<string, object>();
            //parameters["phase"] = (int)phase;
            //parameters["phase_name"] = phase.ToString();

            ////Analytics.CustomEvent("CompletedTutorial", parameters);
            //AnalyticsEvent.TutorialComplete(null, parameters);

            //Dictionary<string, object> parameters = new Dictionary<string, object>()
            //{
            //    { "phase", (int)phase },
            //    { "phase_name", phase.ToString() }
            //};

            Events.CustomData("myTutorialComplete", new Dictionary<string, object>());
#if FB_SDK
            AppManager.I.FacebookManager.LogAppEvent(Facebook.Unity.AppEventName.CompletedTutorial, parameters: parameters);
#endif
        }

        public void TrackSpentBones(int nSpent)
        {
            if (!AnalyticsEnabled)
                return;

            //var parameters = new Dictionary<string, object>();
            //parameters["spent"] = (int)nSpent;

            //Analytics.CustomEvent("SpentCredits", parameters);
            //AnalyticsEvent.ItemSpent(AcquisitionType.Soft, "space", (float)nSpent, "bones");


            var parameters = new Dictionary<string, object>()
            {
                { "bonesSpent", nSpent }
            };

            Events.CustomData("myItemSpent", parameters);

#if FB_SDK
            AppManager.I.FacebookManager.LogAppEvent(Facebook.Unity.AppEventName.SpentCredits, valueToSum: nSpent);
#endif
        }

        public void TrackCustomization(AnturaCustomization customization, float anturaSpacePlayTime)
        {
            if (!AnalyticsEnabled)
                return;

            //var parameters = new Dictionary<string, object>();
            //parameters["customization_json"] = customization.GetJsonListOfIds();
            //parameters["antura_space_play_time"] = anturaSpacePlayTime;

            //Analytics.CustomEvent("custom_antura_customization", parameters);

            var parameters = new Dictionary<string, object>()
            {
                { "customization", customization.GetJsonListOfIds() },
                { "AnturaSpace_playtime", (int)anturaSpacePlayTime }
            };

            Events.CustomData("myAnturaCustomize", parameters);
#if FB_SDK
            AppManager.I.FacebookManager.LogAppEvent("custom_antura_customization", parameters: parameters);
#endif
        }

        public void TrackMiniGameScore(MiniGameCode miniGameCode, int score, JourneyPosition currentJourneyPosition, float duration)
        {
            if (!AnalyticsEnabled)
                return;

            //var parameters = new Dictionary<string, object>();
            //parameters["minigame_code"] = miniGameCode.ToString();
            //parameters["score"] = score;
            //parameters["duration"] = duration;
            //parameters["duration"] = duration;
            //parameters["journey_position"] = currentJourneyPosition.Id;

            ////Analytics.CustomEvent("custom_minigame_score", parameters);
            //AnalyticsEvent.LevelComplete(currentJourneyPosition.Id, parameters);

            var parameters = new Dictionary<string, object>()
            {
                { "minigame", miniGameCode.ToString() },
                { "score", score },
                { "duration", (int)duration },
                { "JP", currentJourneyPosition.Id }
            };

            Events.CustomData("myMinigameScore", parameters);
#if FB_SDK
            AppManager.I.FacebookManager.LogAppEvent("custom_minigame_score", score, parameters);
#endif
        }

        #region Older Events

        public void TrackKioskEvent(string eventName)
        {
            if (AnalyticsEnabled)
            {
                /*var eventData = new Dictionary<string, object>{
                    { "app", "kiosk" },
                    {"lang", (AppManager.I.AppSettings.AppLanguage == AppLanguages.Italian ? "it" : "en")}
                };
                Analytics.CustomEvent(eventName, eventData);*/
            }
        }

        public void TrackGameEvent(LogGamePlayData _data)
        {
            if (AnalyticsEnabled)
            {
                /*var eventName = "GamePlay";
                var evetData = new Dictionary<string, object>{
                    { "uuid", _data.Uuid },
                    { "app", 2 },
                    { "player", 3 }
            };
                Analytics.CustomEvent(eventName, evetData);*/
            }
        }

        public void TrackScene(string sceneName)
        {
            if (AnalyticsEnabled)
            {
                //Analytics.CustomEvent("changeScene", new Dictionary<string, object> { { "scene", sceneName } });
            }
        }

        public void TrackPlayerSession(int age, Profile.PlayerGender gender)
        {
            if (AnalyticsEnabled)
            {
                //Gender playerGender = (gender == Profile.PlayerGender.F ? Gender.Female : Gender.Male);
                //Analytics.SetUserGender(playerGender);
                //int birthYear = DateTime.Now.Year - age;
                //Analytics.SetUserBirthYear(birthYear);
            }
        }

        #endregion

    }
}
