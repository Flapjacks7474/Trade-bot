using System;
using System.Collections.Generic;
using FFTS.Core;

namespace FFTS.Reputation
{
    /// <summary>Street/underground standing. 0-100 except PriceOnHead (dollars).</summary>
    public enum FaceCardAttribute { Respect, Fear, Loyalty, Influence, Notoriety, ThreatLevel }

    /// <summary>Public and industry perception. 0-100.</summary>
    public enum PublicImageAttribute { Popularity, Credibility, Controversy, Professionalism, FanLoyalty, IndustryRelationships }

    public sealed class ReputationChange
    {
        public Dictionary<FaceCardAttribute, int> FaceCard = new Dictionary<FaceCardAttribute, int>();
        public Dictionary<PublicImageAttribute, int> PublicImage = new Dictionary<PublicImageAttribute, int>();
        public long PriceOnHeadDelta;

        public ReputationChange Face(FaceCardAttribute a, int d) { FaceCard[a] = (FaceCard.TryGetValue(a, out var v) ? v : 0) + d; return this; }
        public ReputationChange Image(PublicImageAttribute a, int d) { PublicImage[a] = (PublicImage.TryGetValue(a, out var v) ? v : 0) + d; return this; }
    }

    public sealed class ReputationState
    {
        public Dictionary<FaceCardAttribute, int> FaceCard = new Dictionary<FaceCardAttribute, int>();
        public Dictionary<PublicImageAttribute, int> PublicImage = new Dictionary<PublicImageAttribute, int>();
        public long PriceOnHead;
    }

    /// <summary>
    /// Two independent reputation tracks. The same act can move them in opposite directions —
    /// an underground knockout raises Respect and Fear while lowering Professionalism.
    /// </summary>
    public sealed class ReputationManager
    {
        private readonly EventBus _bus;
        private readonly Dictionary<FaceCardAttribute, int> _face = new Dictionary<FaceCardAttribute, int>();
        private readonly Dictionary<PublicImageAttribute, int> _image = new Dictionary<PublicImageAttribute, int>();
        public long PriceOnHead { get; private set; }

        public ReputationManager(EventBus bus)
        {
            _bus = bus;
            foreach (FaceCardAttribute a in Enum.GetValues(typeof(FaceCardAttribute))) _face[a] = 0;
            foreach (PublicImageAttribute a in Enum.GetValues(typeof(PublicImageAttribute))) _image[a] = 0;
            _image[PublicImageAttribute.Professionalism] = 50; // neutral starting point
            _image[PublicImageAttribute.Credibility] = 20;
        }

        public int Get(FaceCardAttribute a) => _face[a];
        public int Get(PublicImageAttribute a) => _image[a];

        public void Apply(ReputationChange change, string source)
        {
            foreach (var kv in change.FaceCard)
            {
                int old = _face[kv.Key], now = MathUtil.Clamp(old + kv.Value, 0, 100);
                _face[kv.Key] = now;
                if (now != old) _bus?.Publish(new ReputationChanged(source, "face." + kv.Key, old, now));
            }
            foreach (var kv in change.PublicImage)
            {
                int old = _image[kv.Key], now = MathUtil.Clamp(old + kv.Value, 0, 100);
                _image[kv.Key] = now;
                if (now != old) _bus?.Publish(new ReputationChanged(source, "image." + kv.Key, old, now));
            }
            if (change.PriceOnHeadDelta != 0) PriceOnHead = Math.Max(0, PriceOnHead + change.PriceOnHeadDelta);
        }

        public ReputationState Capture() => new ReputationState
        {
            FaceCard = new Dictionary<FaceCardAttribute, int>(_face),
            PublicImage = new Dictionary<PublicImageAttribute, int>(_image),
            PriceOnHead = PriceOnHead
        };

        public void Restore(ReputationState s)
        {
            foreach (var kv in s.FaceCard) _face[kv.Key] = MathUtil.Clamp(kv.Value, 0, 100);
            foreach (var kv in s.PublicImage) _image[kv.Key] = MathUtil.Clamp(kv.Value, 0, 100);
            PriceOnHead = Math.Max(0, s.PriceOnHead);
        }
    }
}
