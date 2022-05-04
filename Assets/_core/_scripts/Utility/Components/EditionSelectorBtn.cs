﻿using Antura.Core;
using System.Collections;
using System.Collections.Generic;
using Antura.Scenes;
using Antura.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antura.Utilities
{
    public class EditionSelectorBtn : MonoBehaviour
    {
        public EditionSelectionManager SelectionManager;

        void Start()
        {
            bool isVisible = AppManager.I.AppEdition.HasMultipleContentEditions;
            gameObject.SetActive(isVisible);

            if (EditionSelectionManager.MustChooseContentEditions)
            {
                // Auto-open switching panel if no player is detected
                SelectionManager.CompleteSelection();
            }
        }

    }
}
