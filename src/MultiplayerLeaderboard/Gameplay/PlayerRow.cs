using System.Globalization;

namespace MultiplayerLeaderboard.Gameplay
{
    /// <summary>
    /// One player's leaderboard row, filled only with values read from the game's multiplayer score sync
    /// state. Accuracy is unknown ("-") while the player has not scored a note yet, and a spectator who
    /// joined after the level started has no score to show.
    ///
    /// Rows are reused for the whole song. The formatted strings are cached against the values that
    /// produced them, so the four-times-per-second refresh does not allocate new strings for unchanged
    /// names, ranks, scores or percentages.
    /// </summary>
    internal sealed class PlayerRow
    {
        public string UserId;
        public string UserName;
        public string CacheKey;
        public int SortIndex;
        public bool IsMe;
        public bool IsParticipant;
        public bool IsFailed;
        public bool HasScoreData;
        public int Score;
        public float Accuracy;
        public bool HasAccuracy;
        public int Rank;
        public int Combo;
        public bool HasCombo;
        public int Mistakes;
        public bool HasMistakes;

        private string _nameCache;
        private string _nameSource;
        private string _rankCache;
        private int _rankSource = int.MinValue;
        private string _accuracyCache;
        private float _accuracySource = float.NaN;
        private bool _accuracyTargetSource;
        private string _scoreCache;
        private int _scoreSource = int.MinValue;
        private bool _scoreParticipantSource;
        private bool _scoreFailedSource;
        private string _comboCache;
        private int _comboSource = int.MinValue;
        private bool _comboTargetSource;
        private string _mistakesCache;
        private int _mistakesSource = int.MinValue;
        private bool _mistakesTargetSource;

        internal string DisplayName
        {
            get
            {
                if (string.Equals(UserName, _nameSource, System.StringComparison.Ordinal) && _nameCache != null)
                {
                    return _nameCache;
                }

                _nameSource = UserName;
                if (string.IsNullOrEmpty(UserName))
                {
                    _nameCache = "(unnamed player)";
                    return _nameCache;
                }

                string trimmed = UserName.Trim();
                _nameCache = trimmed.Length == 0 ? "(unnamed player)" : trimmed;
                return _nameCache;
            }
        }

        internal string RankText
        {
            get
            {
                if (Rank == _rankSource && _rankCache != null)
                {
                    return _rankCache;
                }

                _rankSource = Rank;
                _rankCache = Rank > 0 ? Rank.ToString(CultureInfo.InvariantCulture) : "-";
                return _rankCache;
            }
        }

        internal string AccuracyText
        {
            get
            {
                bool target = !IsParticipant || IsFailed || !HasAccuracy;
                if (target == _accuracyTargetSource &&
                    Accuracy.Equals(_accuracySource) &&
                    _accuracyCache != null)
                {
                    return _accuracyCache;
                }

                _accuracyTargetSource = target;
                _accuracySource = Accuracy;
                _accuracyCache = target
                    ? "-"
                    : Accuracy.ToString("0.00", CultureInfo.InvariantCulture) + "%";
                return _accuracyCache;
            }
        }

        internal string ScoreText
        {
            get
            {
                if (IsParticipant == _scoreParticipantSource &&
                    IsFailed == _scoreFailedSource &&
                    Score == _scoreSource &&
                    _scoreCache != null)
                {
                    return _scoreCache;
                }

                _scoreParticipantSource = IsParticipant;
                _scoreFailedSource = IsFailed;
                _scoreSource = Score;
                _scoreCache = !IsParticipant
                    ? "-"
                    : (IsFailed ? "FAILED" : Score.ToString("N0", CultureInfo.InvariantCulture));
                return _scoreCache;
            }
        }

        internal string ComboText
        {
            get
            {
                bool target = !IsParticipant || !HasCombo;
                if (target == _comboTargetSource && Combo == _comboSource && _comboCache != null)
                {
                    return _comboCache;
                }

                _comboTargetSource = target;
                _comboSource = Combo;
                _comboCache = target ? "-" : Combo.ToString(CultureInfo.InvariantCulture);
                return _comboCache;
            }
        }

        internal string MistakesText
        {
            get
            {
                bool target = !IsParticipant || !HasMistakes;
                if (target == _mistakesTargetSource && Mistakes == _mistakesSource && _mistakesCache != null)
                {
                    return _mistakesCache;
                }

                _mistakesTargetSource = target;
                _mistakesSource = Mistakes;
                _mistakesCache = target ? "-" : Mistakes.ToString(CultureInfo.InvariantCulture);
                return _mistakesCache;
            }
        }
    }
}
