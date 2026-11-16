namespace Ember.EditorTools
{
    /// <summary>Course line shown on the title footer and in the ending credits. Edit and rebuild the scenes.</summary>
    public static class ProjectInfo
    {
        public const string Course = "CMM310 Game Design and Development";
        public const string Year = "2026";

        /// <summary>
        /// The team names are Thai, which TextMeshPro cannot shape and the class Futura font cannot draw, so they are
        /// rendered to this image by Tools/render_team_credits.ps1 from Tools/team.txt.
        /// </summary>
        public const string TeamCreditsImage = EmberPaths.UI + "/T_TeamCredits.png";
    }
}
