using Antura.Database;
using Antura.Helpers;
using Antura.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using ArabicSupport;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Antura.Language
{
    // TODO refactor: this class needs a large refactoring as it is used for several different purposes
    public class ArabicLanguageHelper : AbstractLanguageHelper
    {
        public bool ConvertFarsiYehToAlefMaqsura;

        struct UnicodeLookUpEntry
        {
            public LetterData data;
            public LetterForm form;

            public UnicodeLookUpEntry(Database.LetterData data, Database.LetterForm form)
            {
                this.data = data;
                this.form = form;
            }
        }

        struct DiacriticComboLookUpEntry
        {
            public string symbolID;
            public string LetterID;

            public DiacriticComboLookUpEntry(string symbolID, string LetterID)
            {
                this.symbolID = symbolID;
                this.LetterID = LetterID;
            }
        }

        static List<LetterData> allLetterData;
        static Dictionary<string, UnicodeLookUpEntry> unicodeLookUpCache = new Dictionary<string, UnicodeLookUpEntry>();

        static Dictionary<DiacriticComboLookUpEntry, LetterData> diacriticComboLookUpCache =
            new Dictionary<DiacriticComboLookUpEntry, LetterData>();

        /// <summary>
        /// Collapses diacritics and letters, collapses multiple words variations (e.g. lam + alef), selects correct forms unicodes, and reverses the string.
        /// </summary>
        /// <returns>The string, ready for display or further processing.</returns>
        public override string ProcessString(string str)
        {
            ArabicFixer.ConvertFarsiYehToAlefMaqsura = ConvertFarsiYehToAlefMaqsura;
            return GenericHelper.ReverseText(ArabicFixer.Fix(str, true, true));
        }


        public override List<StringPart> SplitWord(DatabaseObject staticDatabase, WordData wordData,
            bool separateDiacritics = false, bool separateVariations = false, bool keepFormInsideLetter = false)
        {
            // Use ArabicFixer to deal only with combined unicodes
            return AnalyzeArabicString(staticDatabase, ProcessString(wordData.Text), separateDiacritics,
                separateVariations, keepFormInsideLetter);
        }

        public override List<StringPart> SplitPhrase(DatabaseObject staticDatabase, PhraseData phrase,
            bool separateDiacritics = false, bool separateVariations = true, bool keepFormInsideLetter = false)
        {
            // Use ArabicFixer to deal only with combined unicodes
            return AnalyzeArabicString(staticDatabase, ProcessString(phrase.Text), separateDiacritics,
                separateVariations, keepFormInsideLetter);
        }

        #region Private

        List<StringPart> AnalyzeArabicString(DatabaseObject staticDatabase, string processedArabicString,
            bool separateDiacritics = false, bool separateVariations = true, bool keepFormInsideLetter = false)
        {
            if (allLetterData == null)
            {
                allLetterData = new List<LetterData>(staticDatabase.GetLetterTable().GetValuesTyped());

                for (int l = 0; l < allLetterData.Count; ++l)
                {
                    var data = allLetterData[l];

                    foreach (var form in data.GetAvailableForms())
                    {
                        if (data.Kind == LetterDataKind.Letter)
                        {
                            // Overwrite
                            unicodeLookUpCache[data.GetUnicode(form)] = new UnicodeLookUpEntry(data, form);
                        }
                        else
                        {
                            var unicode = data.GetUnicode(form);

                            if (!unicodeLookUpCache.ContainsKey(unicode))
                            {
                                unicodeLookUpCache.Add(unicode, new UnicodeLookUpEntry(data, form));
                            }
                        }
                    }

                    if (data.Kind == LetterDataKind.DiacriticCombo)
                    {
                        diacriticComboLookUpCache.Add(new DiacriticComboLookUpEntry(data.Symbol, data.BaseLetter), data);
                    }
                }
            }

            var result = new List<StringPart>();

            // If we used ArabicFixer, this char array will contain only combined unicodes
            char[] chars = processedArabicString.ToCharArray();

            int stringIndex = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                char character = chars[i];

                // Skip spaces and arabic "?"
                if (character == ' ' || character == '؟')
                {
                    ++stringIndex;
                    continue;
                }

                string unicodeString = GetHexUnicodeFromChar(character);

                if (unicodeString == "0640")
                {
                    // it's an arabic tatweel
                    // just extends previous character
                    for (int t = result.Count - 1; t >= 0; --t)
                    {
                        var previous = result[t];

                        if (previous.toCharacterIndex == stringIndex - 1)
                        {
                            ++previous.toCharacterIndex;
                            result[t] = previous;
                        }
                        else
                        {
                            break;
                        }
                    }

                    ++stringIndex;
                    continue;
                }

                // Find the letter, and check its form
                LetterForm letterForm = LetterForm.None;
                LetterData letterData = null;

                UnicodeLookUpEntry entry;
                if (unicodeLookUpCache.TryGetValue(unicodeString, out entry))
                {
                    letterForm = entry.form;
                    letterData = entry.data;
                    if (keepFormInsideLetter)
                        letterData = letterData.Clone();  // We need to clone the data, as it may be overriden later, if we want to keep forms inside it
                }

                if (letterData != null)
                {
                    if (letterData.Kind == LetterDataKind.DiacriticCombo && separateDiacritics)
                    {
                        // It's a diacritic combo
                        // Separate Letter and Diacritic
                        result.Add(
                            new StringPart(
                                staticDatabase.GetById(staticDatabase.GetLetterTable(), letterData.BaseLetter),
                                stringIndex, stringIndex, letterForm));
                        result.Add(
                            new StringPart(
                                staticDatabase.GetById(staticDatabase.GetLetterTable(), letterData.Symbol),
                                stringIndex, stringIndex, letterForm));
                    }
                    else if (letterData.Kind == LetterDataKind.Symbol &&
                             letterData.Type == LetterDataType.DiacriticSymbol && !separateDiacritics)
                    {
                        // It's a diacritic
                        // Merge Letter and Diacritic

                        var symbolId = letterData.Id;
                        var lastLetterData = result[result.Count - 1];
                        var baseLetterId = lastLetterData.letter.Id;

                        LetterData diacriticLetterData = null;
                        if (AppConfig.DisableShaddah)
                        {
                            if (symbolId == "shaddah")
                            {
                                diacriticLetterData = lastLetterData.letter;
                            }
                            else
                            {
                                diacriticComboLookUpCache.TryGetValue(
                                    new DiacriticComboLookUpEntry(symbolId, baseLetterId), out diacriticLetterData);
                            }
                        }
                        else
                        {
                            diacriticComboLookUpCache.TryGetValue(
                                new DiacriticComboLookUpEntry(symbolId, baseLetterId), out diacriticLetterData);
                        }

                        if (diacriticLetterData == null)
                        {
                            Debug.LogError("Cannot find a single character for " + baseLetterId + " + " + symbolId +
                                           ". Diacritic removed in (" + processedArabicString + ").");
                        }
                        else
                        {
                            var previous = result[result.Count - 1];
                            previous.letter = diacriticLetterData;
                            ++previous.toCharacterIndex;
                            result[result.Count - 1] = previous;
                        }
                    }
                    else if (letterData.Kind == LetterDataKind.LetterVariation && separateVariations &&
                             letterData.BaseLetter == "lam")
                    {
                        // it's a lam-alef combo
                        // Separate Lam and Alef
                        result.Add(
                            new StringPart(
                                staticDatabase.GetById(staticDatabase.GetLetterTable(), letterData.BaseLetter),
                                stringIndex, stringIndex, letterForm));

                        var secondPart = staticDatabase.GetById(staticDatabase.GetLetterTable(), letterData.Symbol);

                        if (secondPart.Kind == LetterDataKind.DiacriticCombo && separateDiacritics)
                        {
                            // It's a diacritic combo
                            // Separate Letter and Diacritic
                            result.Add(
                                new StringPart(
                                    staticDatabase.GetById(staticDatabase.GetLetterTable(), secondPart.BaseLetter),
                                    stringIndex, stringIndex, letterForm));
                            result.Add(
                                new StringPart(
                                    staticDatabase.GetById(staticDatabase.GetLetterTable(), secondPart.Symbol),
                                    stringIndex, stringIndex, letterForm));
                        }
                        else
                        {
                            result.Add(new StringPart(secondPart, stringIndex, stringIndex, letterForm));
                        }
                    }
                    else
                    {
                        result.Add(new StringPart(letterData, stringIndex, stringIndex, letterForm));
                    }
                }
                else
                {
                    Debug.LogWarning($"Cannot parse letter {character} ({unicodeString}) in {processedArabicString}");
                }

                ++stringIndex;
            }

            if (keepFormInsideLetter)
            {
                foreach (var stringPart in result)
                {
                    stringPart.letter.ForcedLetterForm = stringPart.letterForm;
                }
            }
            return result;
        }

        #region Diacritic Fix

        public const string Fathah = "064E";
        public const string Dammah = "064F";
        public const string Kasrah = "0650";
        public const string Sukun = "0652";
        public const string Shaddah = "0651";

        List<string> diacriticsSortOrder = new List<string> { Fathah, Dammah, Kasrah, Sukun, Shaddah };

        private struct DiacriticComboEntry
        {
            public string Unicode1;
            public string Unicode2;

            public DiacriticComboEntry(string _unicode1, string _unicode2)
            {
                Unicode1 = _unicode1;
                Unicode2 = _unicode2;
            }
        }

        private static Dictionary<DiacriticComboEntry, Vector2> DiacriticCombos2Fix = null;

        /// <summary>
        /// these are manually configured positions of diacritic symbols relative to the main letter
        /// since TextMesh Pro can't manage these automatically and some letters are too tall, with the symbol overlapping
        /// </summary>
        private void BuildDiacriticCombos2Fix()
        {
            DiacriticCombos2Fix = new Dictionary<DiacriticComboEntry, Vector2>();

            var diacriticsComboData = LanguageSwitcher.I.GetDiacriticsComboData(LanguageUse.Learning);

            FillCombos2Fix();

            void RefreshEntrySorting(DiacriticEntryKey.Letter entryLetter, LetterData letterData)
            {
                entryLetter.sortNumber = letterData.Number;
                entryLetter.id = letterData.Id;
                entryLetter.page = letterData.Base.Number;
                if (entryLetter.id.StartsWith("alef_hamza"))
                    entryLetter.page = 29;
                if (entryLetter.id.StartsWith("lam_alef"))
                    entryLetter.page = 30;
                if (entryLetter.id.StartsWith("alef_maqsura"))
                    entryLetter.page = 31;
                if (entryLetter.id.StartsWith("hamza"))
                    entryLetter.page = 32;
                if (entryLetter.id.StartsWith("teh_marbuta"))
                    entryLetter.page = 33;
                if (entryLetter.id.StartsWith("yeh_hamza"))
                    entryLetter.page = 34;
                if (letterData.IsOfKindCategory(LetterKindCategory.Symbol))
                    entryLetter.page = 35;
            }

            DiacriticEntryKey.Letter FindLetter(List<LetterData> dbLetters, string unicode)
            {
                var entryLetter = new DiacriticEntryKey.Letter();
                entryLetter.unicode = unicode;
                var l = dbLetters.FirstOrDefault(x => x.Isolated_Unicode == unicode);
                if (l != null)
                {
                    RefreshEntrySorting(entryLetter, l);
                    return entryLetter;
                }
                l = dbLetters.FirstOrDefault(x => x.Initial_Unicode == unicode);
                if (l != null)
                {
                    RefreshEntrySorting(entryLetter, l);
                    return entryLetter;
                }
                l = dbLetters.FirstOrDefault(x => x.Medial_Unicode == unicode);
                if (l != null)
                {
                    RefreshEntrySorting(entryLetter, l);
                    return entryLetter;
                }
                l = dbLetters.FirstOrDefault(x => x.Final_Unicode == unicode);
                if (l != null)
                {
                    RefreshEntrySorting(entryLetter, l);
                    return entryLetter;
                }
                return null;
            }

            // @note: use this to regenerate the data table from the hardcoded values
            if (REPOPULATE_DIACRITIC_ENTRY_TABLE_FROM_HARDCODED_COMBOS)
            {
                var dbLetters = AppManager.I.DB.GetAllLetterData();
                var keys = DiacriticCombos2Fix.Keys.ToArray();
                diacriticsComboData.Keys = new List<DiacriticEntryKey>();
                for (var i = 0; i < keys.Length; i++)
                {
                    var entry = keys[i];
                    var key = new DiacriticEntryKey();
                    diacriticsComboData.Keys.Add(key);

                    key.letter1 = FindLetter(dbLetters, entry.Unicode1);
                    key.letter2 = FindLetter(dbLetters, entry.Unicode2);

                    key.offsetX = (int)DiacriticCombos2Fix[entry].x;
                    key.offsetY = (int)DiacriticCombos2Fix[entry].y;

                }
            }

            // @note: use this to refill the table with all letters in the DB, if not already present
#if UNITY_EDITOR
            if (REFRESH_DIACRITIC_ENTRY_TABLE_FROM_LETTERS_DB)
            {
                var dbLetters = AppManager.I.DB.GetAllLetterData();

                // First, get rid of data that uses diacritics that we do not have
                var nBefore = diacriticsComboData.Keys.Count;
                diacriticsComboData.Keys.RemoveAll(k => !dbLetters.Any(l => l.GetUnicode().Equals(k.letter2.unicode)));
                var nAfter = diacriticsComboData.Keys.Count;

                Debug.LogError("Get rid of " + (nBefore - nAfter) + " wrong diacritic combos");

                var sortedDiacritics = dbLetters.Where(x => x.Type == LetterDataType.DiacriticSymbol).ToList();
                sortedDiacritics.Sort((d1, d2) => d1.Number - d2.Number);

                Debug.LogError("SORTED DIACRITICS: " + sortedDiacritics.ToJoinedString());

                // Then, fill with data from the letter DB
                for (var i = 0; i < dbLetters.Count; i++)
                {
                    var letter = dbLetters[i];
                    if (!letter.Active)
                        continue;
                    bool isSymbol = letter.IsOfKindCategory(LetterKindCategory.Symbol);
                    if (!letter.IsOfKindCategory(LetterKindCategory.BaseAndVariations) && !isSymbol)
                        continue;

                    Debug.LogError("Got Letter: " + letter.Id);

                    foreach (var letterForm in letter.GetAvailableForms())
                    {
                        var formUnicode = letter.GetUnicode(letterForm);
                        Debug.LogError($"With Letter form: {letterForm}({formUnicode})");

                        foreach (var diacritic in sortedDiacritics)
                        {
                            var diacriticUnicode = diacritic.GetUnicode();
                            Debug.LogError($"compare to diacritic: {diacritic}({diacriticUnicode})");

                            bool Comparison(DiacriticEntryKey x)
                            {
                                return x.letter1.unicode.Equals(formUnicode, StringComparison.InvariantCultureIgnoreCase) && x.letter2.unicode.Equals(diacriticUnicode, StringComparison.InvariantCultureIgnoreCase);
                            }

                            if ((isSymbol && diacriticUnicode != Shaddah) || formUnicode == Shaddah)
                            {
                                // Should not appear Remove them if there are
                                Debug.LogError("Removing keys!");
                                diacriticsComboData.Keys.RemoveAll(Comparison);
                                continue;
                            }

                            DiacriticEntryKey key = null;
                            try
                            {
                                if (diacriticsComboData.Keys.Any(Comparison))
                                {
                                    Debug.Log($"We already have key with unicode {formUnicode} and diacritic {diacriticUnicode}");
                                    key = diacriticsComboData.Keys.First(Comparison);

                                    // Refresh the ID with the current DB tho (names of letters may be different)
                                    key.letter1.id = letter.Id;
                                    key.letter2.id = diacritic.Id;
                                }
                                else
                                {
                                    Debug.LogError($"Generating key with unicode {formUnicode} and diacritic {diacriticUnicode}");
                                    key = new DiacriticEntryKey
                                    {
                                        letter1 = FindLetter(dbLetters, formUnicode),
                                        letter2 = FindLetter(dbLetters, diacriticUnicode)
                                    };
                                    diacriticsComboData.Keys.Add(key);
                                }
                            }
                            catch (System.Exception e) { Debug.LogWarning($"Ignoring exception: {e.Message}"); }

                            // Refresh page & sorting
                            //Debug.LogError("Check " + key.letter1.id + " " + key.letter2.id);
                            try
                            { if (key != null) RefreshEntrySorting(key.letter1, AppManager.I.DB.GetLetterDataById(key.letter1.id)); }
                            catch (System.Exception e) { Debug.LogWarning($"Ignoring exception: {e.Message}"); }
                            try
                            { if (key != null) RefreshEntrySorting(key.letter2, AppManager.I.DB.GetLetterDataById(key.letter2.id)); }
                            catch (System.Exception e) { Debug.LogWarning($"Ignoring exception: {e.Message}"); }
                        }
                    }
                }

            }
#endif

            if (REFRESH_DIACRITIC_ENTRY_TABLE_FROM_LETTERS_DB || REPOPULATE_DIACRITIC_ENTRY_TABLE_FROM_HARDCODED_COMBOS)
            {
                // Auto-sort the data like in the book
                diacriticsComboData.Keys = diacriticsComboData.Keys.OrderBy(key =>
                {
                    var letter = AppManager.I.DB.GetLetterDataById(key.letter1.id);
                    if (letter == null)
                        return 0;
                    var symbolOrder = diacriticsSortOrder.IndexOf(key.letter2.unicode);
                    switch (letter.Kind)
                    {
                        case LetterDataKind.LetterVariation:
                            return 10000 + symbolOrder;
                        case LetterDataKind.Symbol:
                            return 20000 + diacriticsSortOrder.IndexOf(key.letter1.unicode) * 100 + symbolOrder;
                            ;
                        default:
                            return key.letter1.sortNumber * 100 + symbolOrder;
                    }
                }).ToList();

#if UNITY_EDITOR
                EditorUtility.SetDirty(diacriticsComboData);
#endif
            }

            // Use the values in the data table instead
            DiacriticCombos2Fix.Clear();
            foreach (var key in diacriticsComboData.Keys)
            {
                DiacriticCombos2Fix.Add(new DiacriticComboEntry(key.letter1.unicode, key.letter2.unicode), new Vector2(key.offsetX, key.offsetY));
            }
        }


        private static bool REPOPULATE_DIACRITIC_ENTRY_TABLE_FROM_HARDCODED_COMBOS = false;
        private static bool REFRESH_DIACRITIC_ENTRY_TABLE_FROM_LETTERS_DB = false;

        private Vector2 FindDiacriticCombo2Fix(string Unicode1, string Unicode2)
        {
            if (DiacriticCombos2Fix == null)
            {
                BuildDiacriticCombos2Fix();
            }

            Vector2 newDelta = new Vector2(0, 0);
            var diacriticsComboData = LanguageSwitcher.I.GetDiacriticsComboData(LanguageUse.Learning);
            var combo = diacriticsComboData.Keys.FirstOrDefault(x => x.letter1.unicode == Unicode1
                                                                     && x.letter2.unicode == Unicode2);
            if (combo != null)
            {
                newDelta.x = combo.offsetX;
                newDelta.y = combo.offsetY;
            }
            return newDelta;
        }

        /// <summary>
        /// Adjusts the diacritic positions.
        /// </summary>
        /// <returns><c>true</c>, if diacritic positions was adjusted, <c>false</c> otherwise.</returns>
        /// <param name="textInfo">Text info.</param>
        public override bool FixTMProDiacriticPositions(TMPro.TMP_TextInfo textInfo)
        {
            int characterCount = textInfo.characterCount;
            if (characterCount <= 1)
                return false;

            bool changed = false;
            for (int charPosition = 0; charPosition < characterCount - 1; charPosition++)
            {
                var char1Pos = charPosition;
                var char2Pos = charPosition + 1;
                var UnicodeChar1 = GetHexUnicodeFromChar(textInfo.characterInfo[char1Pos].character);
                var UnicodeChar2 = GetHexUnicodeFromChar(textInfo.characterInfo[char2Pos].character);

                changed = true;


                bool char1IsDiacritic = (UnicodeChar1 == Dammah || UnicodeChar1 == Fathah || UnicodeChar1 == Sukun || UnicodeChar1 == Kasrah);
                bool char2IsDiacritic = (UnicodeChar2 == Dammah || UnicodeChar2 == Fathah || UnicodeChar2 == Sukun || UnicodeChar2 == Kasrah);

                bool char1IsShaddah = UnicodeChar1 == Shaddah;
                bool char2IsShaddah = UnicodeChar2 == Shaddah;

                if (char1IsDiacritic && char2IsShaddah)
                {
                    CopyPosition(textInfo, char2Pos, char1Pos);             // Place the diacritic where the shaddah is
                    ApplyOffset(textInfo, char1Pos, FindDiacriticCombo2Fix(UnicodeChar1, UnicodeChar2));    // then, move the diacritic in respect to the shaddah using the delta
                }
                else if (char1IsShaddah && char2IsDiacritic)
                {
                    CopyPosition(textInfo, char1Pos, char2Pos);             // Place the diacritic where the shaddah is
                    ApplyOffset(textInfo, char2Pos, FindDiacriticCombo2Fix(UnicodeChar2, UnicodeChar1));    // then, move the diacritic in respect to the shaddah using the delta
                }
                else
                {
                    // Move the symbol in respect to the base letter
                    //Debug.LogError($"Mod for {UnicodeChar1} to {UnicodeChar2}: {modificationDelta}");
                    ApplyOffset(textInfo, char2Pos, FindDiacriticCombo2Fix(UnicodeChar1, UnicodeChar2));

                    // If we get a Diacritic and the next char is a Shaddah, however, we need to instead first move the shaddah, then move the diacritic in respect to the shaddah
                    if (charPosition < characterCount - 2)
                    {
                        var UnicodeChar3 = GetHexUnicodeFromChar(textInfo.characterInfo[charPosition + 2].character);
                        bool char3IsDiacritic = (UnicodeChar3 == Dammah || UnicodeChar3 == Fathah || UnicodeChar3 == Sukun || UnicodeChar3 == Kasrah);
                        bool char3IsShaddah = UnicodeChar3 == Shaddah;
                        var char3Pos = charPosition + 2;

                        // Place this Shaddah in respect to the letter
                        if (char2IsDiacritic && char3IsShaddah)
                        {
                            ApplyOffset(textInfo, char3Pos, FindDiacriticCombo2Fix(UnicodeChar1, UnicodeChar3));
                        }
                        else if (char2IsShaddah && char3IsDiacritic)
                        {
                            Debug.LogError(textInfo.textComponent.text + " " + " has weird diacritic");

                            ApplyOffset(textInfo, char2Pos, FindDiacriticCombo2Fix(UnicodeChar1, UnicodeChar2));
                        }
                    }

                }
            }
            return changed;
        }

        private void ApplyOffset(TMPro.TMP_TextInfo textInfo, int charPosition, Vector2 modificationDelta)
        {
            int materialIndex2 = textInfo.characterInfo[charPosition].materialReferenceIndex;
            int vertexIndex2 = textInfo.characterInfo[charPosition].vertexIndex;
            Vector3[] Vertices2 = textInfo.meshInfo[materialIndex2].vertices;

            float charsize2 = (Vertices2[vertexIndex2 + 2].y - Vertices2[vertexIndex2 + 0].y);
            float dx2 = charsize2 * modificationDelta.x / 100f;
            float dy2 = charsize2 * modificationDelta.y / 100f;
            Vector3 offset2 = new Vector3(dx2, dy2, 0f);

            Vertices2[vertexIndex2 + 0] = Vertices2[vertexIndex2 + 0] + offset2;
            Vertices2[vertexIndex2 + 1] = Vertices2[vertexIndex2 + 1] + offset2;
            Vertices2[vertexIndex2 + 2] = Vertices2[vertexIndex2 + 2] + offset2;
            Vertices2[vertexIndex2 + 3] = Vertices2[vertexIndex2 + 3] + offset2;
        }

        private void CopyPosition(TMPro.TMP_TextInfo textInfo, int charFrom, int charTo)
        {
            int materialIndex2 = textInfo.characterInfo[charTo].materialReferenceIndex;
            int vertexIndex2 = textInfo.characterInfo[charTo].vertexIndex;
            Vector3[] Vertices2 = textInfo.meshInfo[materialIndex2].vertices;

            int materialIndex1 = textInfo.characterInfo[charFrom].materialReferenceIndex;
            int vertexIndex1 = textInfo.characterInfo[charFrom].vertexIndex;
            Vector3[] Vertices1 = textInfo.meshInfo[materialIndex1].vertices;

            Vertices2[vertexIndex2 + 0] = Vertices1[vertexIndex1 + 0];
            Vertices2[vertexIndex2 + 1] = Vertices1[vertexIndex1 + 1];
            Vertices2[vertexIndex2 + 2] = Vertices1[vertexIndex1 + 2];
            Vertices2[vertexIndex2 + 3] = Vertices1[vertexIndex1 + 3];
        }

        public override string DebugShowDiacriticFix(string unicode1, string unicode2)
        {
            var delta = FindDiacriticCombo2Fix(unicode1, unicode2);
            return
                string.Format(
                    "DiacriticCombos2Fix.Add(new DiacriticComboEntry(\"{0}\", \"{1}\"), new Vector2({2}, {3}));",
                    unicode1, unicode2, delta.x, delta.y);
        }

        #endregion


        #endregion

        private void FillCombos2Fix()
        {
            #region Hardcoded Combo Values
            //////// LETTER alef
            // alef_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0627", "064E"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE8E", "064E"), new Vector2(-30, 80));
            // alef_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0627", "064F"), new Vector2(-30, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE8E", "064F"), new Vector2(-50, 80));
            // alef_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0627", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE8E", "0650"), new Vector2(0, 0));

            //////// LETTER beh
            // beh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0628", "064E"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE91", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE92", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE90", "064E"), new Vector2(150, 0));
            // beh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0628", "064F"), new Vector2(130, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE91", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE92", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE90", "064F"), new Vector2(130, 0));
            // beh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0628", "0650"), new Vector2(160, -130));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE91", "0650"), new Vector2(0, -130));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE92", "0650"), new Vector2(0, -130));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE90", "0650"), new Vector2(160, -130));
            // beh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0628", "0652"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE91", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE92", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE90", "0652"), new Vector2(150, 0));

            // beh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0628", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE91", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE92", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE90", "0651"), new Vector2(0, 0));

            //////// LETTER teh
            // teh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062A", "0652"), new Vector2(150, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE97", "0652"), new Vector2(0, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE98", "0652"), new Vector2(0, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE96", "0652"), new Vector2(150, 30));
            // teh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062A", "0650"), new Vector2(160, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE97", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE98", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE96", "0650"), new Vector2(160, 0));
            // teh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062A", "064E"), new Vector2(90, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE97", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE98", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE96", "064E"), new Vector2(90, 0));
            // teh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062A", "064F"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE97", "064F"), new Vector2(-10, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE98", "064F"), new Vector2(-10, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE96", "064F"), new Vector2(60, 0));

            // teh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062A", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE97", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE98", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE96", "0651"), new Vector2(0, 0));

            //////// LETTER theh
            // theh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062B", "0650"), new Vector2(160, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9B", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9C", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9A", "0650"), new Vector2(160, 0));
            // theh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062B", "064E"), new Vector2(100, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9B", "064E"), new Vector2(-20, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9C", "064E"), new Vector2(-20, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9A", "064E"), new Vector2(60, 0));
            // theh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062B", "0652"), new Vector2(80, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9B", "0652"), new Vector2(0, 90));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9C", "0652"), new Vector2(0, 90));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9A", "0652"), new Vector2(80, 70));
            // theh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062B", "064F"), new Vector2(60, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9B", "064F"), new Vector2(-30, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9C", "064F"), new Vector2(-30, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9A", "064F"), new Vector2(60, 40));

            // theh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062B", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9B", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9C", "0651"), new Vector2(0, 120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9A", "0651"), new Vector2(0, 0));

            //////// LETTER jeem
            // jeem_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062C", "064E"), new Vector2(60, -30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9F", "064E"), new Vector2(20, -30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA0", "064E"), new Vector2(20, -30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9E", "064E"), new Vector2(60, -30));
            // jeem_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062C", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9F", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA0", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9E", "064F"), new Vector2(0, 0));
            // jeem_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062C", "0650"), new Vector2(90, -260));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9F", "0650"), new Vector2(30, -110));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA0", "0650"), new Vector2(30, -110));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9E", "0650"), new Vector2(90, -260));
            // jeem_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062C", "0652"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9F", "0652"), new Vector2(20, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA0", "0652"), new Vector2(20, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9E", "0652"), new Vector2(50, 0));
            // jeem_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062C", "0651"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9F", "0651"), new Vector2(20, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA0", "0651"), new Vector2(20, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE9E", "0651"), new Vector2(50, 0));

            //////// LETTER hah
            // hah_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062D", "064E"), new Vector2(60, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA3", "064E"), new Vector2(30, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA4", "064E"), new Vector2(30, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA2", "064E"), new Vector2(60, -40));
            // hah_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062D", "064F"), new Vector2(50, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA3", "064F"), new Vector2(10, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA4", "064F"), new Vector2(40, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA2", "064F"), new Vector2(40, -40));
            // hah_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062D", "0650"), new Vector2(70, -300));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA3", "0650"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA4", "0650"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA2", "0650"), new Vector2(70, -300));
            // hah_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062D", "0652"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA3", "0652"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA4", "0652"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA2", "0652"), new Vector2(60, 0));
            // hah_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062D", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA3", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA4", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA2", "0651"), new Vector2(0, 0));

            //////// LETTER 7 khah
            // khah_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062E", "0652"), new Vector2(70, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA7", "0652"), new Vector2(50, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA8", "0652"), new Vector2(50, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA6", "0652"), new Vector2(70, 40));
            // khah_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062E", "064F"), new Vector2(40, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA7", "064F"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA8", "064F"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA6", "064F"), new Vector2(40, 40));
            // khah_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062E", "064E"), new Vector2(40, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA7", "064E"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA8", "064E"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA6", "064E"), new Vector2(40, 40));
            // khah_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062E", "0651"), new Vector2(40, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA7", "0651"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA8", "0651"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA6", "0651"), new Vector2(40, 40));
            // khah_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062E", "0650"), new Vector2(80, -260));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA7", "0650"), new Vector2(5, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA8", "0650"), new Vector2(5, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEA6", "0650"), new Vector2(80, -260));

            //////// LETTER 8 dal
            // dal_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062F", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAA", "0652"), new Vector2(0, 0));
            // dal_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062F", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAA", "0651"), new Vector2(0, 0));
            // dal_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062F", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAA", "064F"), new Vector2(0, 0));
            // dal_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062F", "0650"), new Vector2(20, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAA", "0650"), new Vector2(20, 0));
            // dal_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("062F", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAA", "064E"), new Vector2(0, 0));

            //////// LETTER thal
            // thal_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0630", "064E"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAC", "064E"), new Vector2(0, 80));
            // thal_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0630", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAC", "064F"), new Vector2(0, 80));
            // thal_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0630", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAC", "0650"), new Vector2(0, 0));
            // thal_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0630", "0652"), new Vector2(20, 110));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAC", "0652"), new Vector2(20, 110));
            // thal_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0630", "0651"), new Vector2(20, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAC", "0651"), new Vector2(20, 80));

            //////// LETTER reh
            // reh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0631", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAE", "0651"), new Vector2(0, 0));
            // reh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0631", "0650"), new Vector2(50, -200));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAE", "0650"), new Vector2(50, -200));
            // reh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0631", "0652"), new Vector2(100, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAE", "0652"), new Vector2(100, 0));
            // reh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0631", "064E"), new Vector2(100, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAE", "064E"), new Vector2(100, 0));
            // reh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0631", "064F"), new Vector2(80, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEAE", "064F"), new Vector2(80, 0));

            //////// LETTER zain
            // zain_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0632", "064F"), new Vector2(60, 10));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB0", "064F"), new Vector2(60, 10));
            // zain_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0632", "064E"), new Vector2(90, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB0", "064E"), new Vector2(90, 0));
            // zain_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0632", "0650"), new Vector2(70, -180));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB0", "0650"), new Vector2(70, -180));
            // zain_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0632", "0652"), new Vector2(80, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB0", "0652"), new Vector2(80, 20));
            // zain_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0632", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB0", "0651"), new Vector2(0, 0));

            //////// LETTER 12 seen
            // seen_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0633", "064F"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB3", "064F"), new Vector2(100, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB4", "064F"), new Vector2(100, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB2", "064F"), new Vector2(200, 0));
            // seen_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0633", "064E"), new Vector2(300, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB3", "064E"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB4", "064E"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB2", "064E"), new Vector2(300, 0));
            // seen_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0633", "0650"), new Vector2(80, -160));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB3", "0650"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB4", "0650"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB2", "0650"), new Vector2(80, -160));
            // seen_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0633", "0652"), new Vector2(300, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB3", "0652"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB4", "0652"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB2", "0652"), new Vector2(300, 0));
            // seen_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0633", "0651"), new Vector2(300, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB3", "0651"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB4", "0651"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB2", "0651"), new Vector2(300, 0));

            //////// LETTER 13 sheen
            // sheen_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0634", "064F"), new Vector2(210, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB7", "064F"), new Vector2(110, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB8", "064F"), new Vector2(110, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB6", "064F"), new Vector2(230, 60));
            // sheen_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0634", "064E"), new Vector2(330, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB7", "064E"), new Vector2(180, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB8", "064E"), new Vector2(180, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB6", "064E"), new Vector2(340, 60));
            // sheen_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0634", "0650"), new Vector2(340, -30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB7", "0650"), new Vector2(100, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB8", "0650"), new Vector2(100, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB6", "0650"), new Vector2(340, -30));
            // sheen_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0634", "0652"), new Vector2(300, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB7", "0652"), new Vector2(160, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB8", "0652"), new Vector2(160, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB6", "0652"), new Vector2(300, 70));
            // sheen_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0634", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB7", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB8", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEB6", "0651"), new Vector2(0, 0));

            //////// LETTER 14 sad
            // sad_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0635", "064F"), new Vector2(300, -50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBB", "064F"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBC", "064F"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBA", "064F"), new Vector2(300, 0));
            // sad_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0635", "064E"), new Vector2(400, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBB", "064E"), new Vector2(300, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBC", "064E"), new Vector2(300, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBA", "064E"), new Vector2(400, 0));
            // sad_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0635", "0650"), new Vector2(500, -60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBB", "0650"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBC", "0650"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBA", "0650"), new Vector2(500, -60));
            // sad_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0635", "0652"), new Vector2(380, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBB", "0652"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBC", "0652"), new Vector2(200, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBA", "0652"), new Vector2(380, 0));
            // sad_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0635", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBB", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBC", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBA", "0651"), new Vector2(0, 0));

            //////// LETTER 15 dad
            // dad_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0636", "0652"), new Vector2(300, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBF", "0652"), new Vector2(160, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC0", "0652"), new Vector2(180, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBE", "0652"), new Vector2(250, 0));
            // dad_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0636", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBF", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC0", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBE", "0651"), new Vector2(0, 0));
            // dad_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0636", "0650"), new Vector2(400, -130));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBF", "0650"), new Vector2(150, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC0", "0650"), new Vector2(180, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBE", "0650"), new Vector2(400, -130));
            // dad_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0636", "064E"), new Vector2(400, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBF", "064E"), new Vector2(190, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC0", "064E"), new Vector2(210, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBE", "064E"), new Vector2(400, 0));
            // dad_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0636", "064F"), new Vector2(220, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBF", "064F"), new Vector2(145, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC0", "064F"), new Vector2(165, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEBE", "064F"), new Vector2(235, 0));

            //////// LETTER 16 tah
            // tah_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0637", "064E"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC3", "064E"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC4", "064E"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC2", "064E"), new Vector2(0, 100));
            // tah_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0637", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC3", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC4", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC2", "064F"), new Vector2(0, 80));
            // tah_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0637", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC3", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC4", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC2", "0651"), new Vector2(0, 100));
            // tah_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0637", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC3", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC4", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC2", "0650"), new Vector2(0, 0));
            // tah_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0637", "0652"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC3", "0652"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC4", "0652"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC2", "0652"), new Vector2(0, 100));

            //////// LETTER 17 zah
            // zah_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0638", "0652"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC7", "0652"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC8", "0652"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC6", "0652"), new Vector2(0, 100));
            // zah_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0638", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC7", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC8", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC6", "064F"), new Vector2(0, 80));
            // zah_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0638", "064E"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC7", "064E"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC8", "064E"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC6", "064E"), new Vector2(0, 80));
            // zah_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0638", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC7", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC8", "0651"), new Vector2(0, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC6", "0651"), new Vector2(0, 100));
            // zah_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0638", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC7", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC8", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEC6", "0650"), new Vector2(0, 0));

            //////// LETTER 18ain
            // ain_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0639", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECB", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECC", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECA", "064E"), new Vector2(0, 0));
            // ain_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0639", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECB", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECC", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECA", "064F"), new Vector2(0, 0));
            // ain_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0639", "0650"), new Vector2(120, -250));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECB", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECC", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECA", "0650"), new Vector2(20, -300));
            // ain_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0639", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECB", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECC", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECA", "0652"), new Vector2(0, 0));
            // ain_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0639", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECB", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECC", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECA", "0651"), new Vector2(0, 0));

            //////// LETTER 19 ghain
            // ghain_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("063A", "0651"), new Vector2(40, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECF", "0651"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED0", "0651"), new Vector2(20, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECE", "0651"), new Vector2(40, 20));
            // ghain_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("063A", "0650"), new Vector2(70, -250));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECF", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED0", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECE", "0650"), new Vector2(50, -250));
            // ghain_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("063A", "0652"), new Vector2(40, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECF", "0652"), new Vector2(20, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED0", "0652"), new Vector2(20, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECE", "0652"), new Vector2(40, 40));
            // ghain_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("063A", "064E"), new Vector2(0, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECF", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED0", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECE", "064E"), new Vector2(0, 0));
            // ghain_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("063A", "064F"), new Vector2(0, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECF", "064F"), new Vector2(0, 10));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED0", "064F"), new Vector2(0, 10));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FECE", "064F"), new Vector2(20, 10));

            //////// LETTER 20 feh
            // feh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0641", "064F"), new Vector2(180, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED3", "064F"), new Vector2(0, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED4", "064F"), new Vector2(0, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED2", "064F"), new Vector2(180, 30));
            // feh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0641", "064E"), new Vector2(300, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED3", "064E"), new Vector2(50, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED4", "064E"), new Vector2(50, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED2", "064E"), new Vector2(300, 20));
            // feh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0641", "0651"), new Vector2(80, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED3", "0651"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED4", "0651"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED2", "0651"), new Vector2(80, 80));
            // feh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0641", "0650"), new Vector2(330, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED3", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED4", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED2", "0650"), new Vector2(350, 0));
            // feh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0641", "0652"), new Vector2(250, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED3", "0652"), new Vector2(40, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED4", "0652"), new Vector2(40, 70));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED2", "0652"), new Vector2(250, 60));

            //////// LETTER 21 qaf
            // qaf_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0642", "0651"), new Vector2(90, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED7", "0651"), new Vector2(0, 90));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED8", "0651"), new Vector2(0, 90));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED6", "0651"), new Vector2(90, 40));
            // qaf_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0642", "0650"), new Vector2(50, -140));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED7", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED8", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED6", "0650"), new Vector2(50, -140));
            // qaf_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0642", "0652"), new Vector2(80, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED7", "0652"), new Vector2(0, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED8", "0652"), new Vector2(0, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED6", "0652"), new Vector2(80, 60));
            // qaf_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0642", "064E"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED7", "064E"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED8", "064E"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED6", "064E"), new Vector2(50, 0));
            // qaf_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0642", "064F"), new Vector2(80, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED7", "064F"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED8", "064F"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FED6", "064F"), new Vector2(80, 20));

            //////// LETTER 22 kaf
            // kaf_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0643", "064F"), new Vector2(40, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDB", "064F"), new Vector2(0, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDC", "064F"), new Vector2(0, 30));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDA", "064F"), new Vector2(40, 0));
            // kaf_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0643", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDB", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDC", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDA", "064E"), new Vector2(0, 0));
            // kaf_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0643", "0650"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDB", "0650"), new Vector2(20, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDC", "0650"), new Vector2(30, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDA", "0650"), new Vector2(60, 0));
            // kaf_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0643", "0651"), new Vector2(50, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDB", "0651"), new Vector2(20, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDC", "0651"), new Vector2(20, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDA", "0651"), new Vector2(50, 20));
            // kaf_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0643", "0652"), new Vector2(50, 20));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDB", "0652"), new Vector2(20, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDC", "0652"), new Vector2(20, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDA", "0652"), new Vector2(50, 20));

            //////// LETTER 23 lam
            // lam_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0644", "0650"), new Vector2(0, -100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDF", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE0", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDE", "0650"), new Vector2(0, -100));
            // lam_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0644", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDF", "064E"), new Vector2(40, 120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE0", "064E"), new Vector2(40, 120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDE", "064E"), new Vector2(0, 0));
            // lam_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0644", "0651"), new Vector2(40, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDF", "0651"), new Vector2(0, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE0", "0651"), new Vector2(0, 60));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDE", "0651"), new Vector2(40, 40));
            // lam_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0644", "0652"), new Vector2(140, 100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDF", "0652"), new Vector2(40, 140));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE0", "0652"), new Vector2(40, 140));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDE", "0652"), new Vector2(140, 100));
            // lam_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0644", "064F"), new Vector2(80, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDF", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE0", "064F"), new Vector2(0, 80));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEDE", "064F"), new Vector2(80, 40));

            //////// LETTER 24 meem
            // meem_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0645", "0650"), new Vector2(60, -100));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE3", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE4", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE2", "0650"), new Vector2(60, -100));
            // meem_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0645", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE3", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE4", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE2", "064E"), new Vector2(0, 0));
            // meem_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0645", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE3", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE4", "0651"), new Vector2(0, 120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE2", "0651"), new Vector2(0, 0));
            // meem_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0645", "0652"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE3", "0652"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE4", "0652"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE2", "0652"), new Vector2(50, 0));
            // meem_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0645", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE3", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE4", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE2", "064F"), new Vector2(0, 0));

            //////// LETTER 25 noon
            // noon_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0646", "064F"), new Vector2(30, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE7", "064F"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE8", "064F"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE6", "064F"), new Vector2(40, 0));
            // noon_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0646", "0650"), new Vector2(60, -150));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE7", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE8", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE6", "0650"), new Vector2(60, -150));
            // noon_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0646", "064E"), new Vector2(80, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE7", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE8", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE6", "064E"), new Vector2(80, 0));
            // noon_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0646", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE7", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE8", "0651"), new Vector2(0, 40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE6", "0651"), new Vector2(0, 0));
            // noon_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0646", "0652"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE7", "0652"), new Vector2(30, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE8", "0652"), new Vector2(30, 50));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEE6", "0652"), new Vector2(50, 0));

            //////// LETTER heh
            // heh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0647", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEB", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEC", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEA", "064E"), new Vector2(0, 0));
            // heh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0647", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEB", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEC", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEA", "064F"), new Vector2(0, 0));
            // heh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0647", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEB", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEC", "0650"), new Vector2(0, -120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEA", "0650"), new Vector2(0, 0));
            // heh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0647", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEB", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEC", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEA", "0652"), new Vector2(0, 0));
            // heh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0647", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEB", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEC", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEA", "0651"), new Vector2(0, 0));

            //////// LETTER waw
            // waw_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0648", "064F"), new Vector2(50, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEE", "064F"), new Vector2(50, 0));
            // waw_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0648", "064E"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEE", "064E"), new Vector2(60, 0));
            // waw_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0648", "0650"), new Vector2(60, -140));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEE", "0650"), new Vector2(60, -140));
            // waw_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0648", "0652"), new Vector2(60, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEE", "0652"), new Vector2(60, 0));
            // waw_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0648", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEEE", "0651"), new Vector2(0, 0));

            //////// LETTER yeh
            // yeh_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064A", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF3", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF4", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF2", "064E"), new Vector2(0, 0));
            // yeh_shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064A", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF3", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF4", "0651"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF2", "0651"), new Vector2(0, 0));
            // yeh_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064A", "0650"), new Vector2(140, -230));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF3", "0650"), new Vector2(0, -120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF4", "0650"), new Vector2(0, -120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF2", "0650"), new Vector2(120, -260));
            // yeh_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064A", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF3", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF4", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF2", "064F"), new Vector2(0, 0));
            // yeh_sukun
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064A", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF3", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF4", "0652"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF2", "0652"), new Vector2(0, 0));

            //////// LETTER alef_hamza_hi
            // alef_hamza_hi_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0623", "064E"), new Vector2(0, 200));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE84", "064E"), new Vector2(-20, 200));
            // alef_hamza_hi_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0623", "064F"), new Vector2(-20, 130));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE84", "064F"), new Vector2(-20, 130));

            //////// LETTER alef_hamza_low
            // alef_hamza_low_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0625", "0650"), new Vector2(0, -120));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE88", "0650"), new Vector2(0, -120));

            //////// LETTER lam_alef_kasrah
            // lam_alef_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF5", "0650"), new Vector2(120, -40));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEF6", "0650"), new Vector2(100, 0));

            //////// LETTER teh_marbuta
            // teh_marbuta_dammah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0629", "064F"), new Vector2(0, 0));
            //DiacriticCombos2Fix.Add(new DiacriticComboEntry("", "064F"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE94", "064F"), new Vector2(0, 80));
            // teh_marbuta_fathah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0629", "064E"), new Vector2(0, 0));
            //DiacriticCombos2Fix.Add(new DiacriticComboEntry("", "064E"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE94", "064E"), new Vector2(0, 80));
            // teh_marbuta_kasrah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0629", "0650"), new Vector2(0, 0));
            //DiacriticCombos2Fix.Add(new DiacriticComboEntry("", "0650"), new Vector2(0, 0));
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FE94", "0650"), new Vector2(0, 0));

            //////// SYMBOL shaddah
            /// fathah  -> shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064E", "0651"), new Vector2(0, 100));
            /// dammah -> shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("064F", "0651"), new Vector2(0, 100));
            /// kasrah  -> shaddah
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("0650", "0651"), new Vector2(0, -80));

            ///lam alef final
            DiacriticCombos2Fix.Add(new DiacriticComboEntry("FEFC", "0651"), new Vector2(20, 130));
            #endregion
        }
    }
}
