using FFTS.Combat;
using FFTS.Relationships;
using FFTS.Reputation;

namespace FFTS.Session
{
    public enum VenueType { Underground, Sanctioned, Street }

    /// <summary>Maps a fight's outcome and context to reputation and relationship consequences.</summary>
    public static class FightConsequences
    {
        public static ReputationChange ReputationFor(VenueType venue, bool playerWon, FightEndMethod method)
        {
            var c = new ReputationChange();
            bool finisher = method == FightEndMethod.Finisher;
            switch (venue)
            {
                case VenueType.Underground:
                    if (playerWon) c.Face(FaceCardAttribute.Respect, 4).Face(FaceCardAttribute.Fear, finisher ? 4 : 2).Face(FaceCardAttribute.Notoriety, 2)
                                    .Image(PublicImageAttribute.Professionalism, -1);
                    else c.Face(FaceCardAttribute.Respect, -2);
                    break;
                case VenueType.Sanctioned:
                    if (playerWon) c.Face(FaceCardAttribute.Respect, 2).Image(PublicImageAttribute.Popularity, 3).Image(PublicImageAttribute.Professionalism, 1);
                    else c.Image(PublicImageAttribute.Popularity, -1);
                    break;
                default: // public street fight
                    if (playerWon) c.Face(FaceCardAttribute.Notoriety, 3).Face(FaceCardAttribute.Fear, 2)
                                    .Image(PublicImageAttribute.Credibility, -2).Image(PublicImageAttribute.Professionalism, -3).Image(PublicImageAttribute.Controversy, 2);
                    else c.Face(FaceCardAttribute.Respect, -3).Image(PublicImageAttribute.Controversy, 1);
                    break;
            }
            return c;
        }

        /// <summary>How the opponent's view of the player changes.</summary>
        public static RelationshipDelta OpponentDelta(bool playerWon, FightEndMethod method) => playerWon
            ? new RelationshipDelta { Respect = 8, Resentment = method == FightEndMethod.Finisher ? 15 : 8, Rivalry = 10, Fear = 5 }
            : new RelationshipDelta { Respect = -3, Rivalry = 4 };
    }
}
