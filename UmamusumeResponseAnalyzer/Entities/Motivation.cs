namespace UmamusumeResponseAnalyzer.Entities
{
    public class Motivation(int m)
    {
        public static Motivation Best => new(5);
        public static Motivation Good => new(4);
        public static Motivation Normal => new(3);
        public static Motivation Bad => new(2);
        public static Motivation Worst => new(1);

        private readonly int motivation = m;
        private string enumString => motivation switch
        {
            1 => Localization.Game.I18N_MotivationWorst,
            2 => Localization.Game.I18N_MotivationBad,
            3 => Localization.Game.I18N_MotivationNormal,
            4 => Localization.Game.I18N_MotivationGood,
            5 => Localization.Game.I18N_MotivationBest
        };

        public static implicit operator int(Motivation m) => m.motivation;
        public static implicit operator string(Motivation m) => m.enumString;
        public override string ToString() => enumString;
    }
}
