using System.Collections;
using Antura.Core;
using Antura.Database;
using Antura.Language;
using UnityEngine;

namespace Antura.UI
{
    public class SelectNativeLanguageButton : MonoBehaviour
    {
        public TextRender nameText;
        public UIButton button;

        public SelectNativeLanguagePanel parentPanel;

        private LanguageCode languageCode;
        public LanguageCode LanguageCode => languageCode;

        public void Setup(LanguageCode languageCode)
        {
            this.languageCode = languageCode;

            var loc = LocalizationManager.GetLocalizationData(LocalizationDataId.Game_Title_2);
            nameText.OverridenLanguageCode = languageCode;
            nameText.SetText(loc.GetLocalized(languageCode).Text);
        }

        public void OnClick()
        {
            parentPanel.ConfirmSelection(languageCode);
        }

        public void SetUnselected()
        {
            button.Bt.interactable = true;
        }
        public void SetSelected()
        {
            button.Bt.interactable = false;
        }
    }
}
