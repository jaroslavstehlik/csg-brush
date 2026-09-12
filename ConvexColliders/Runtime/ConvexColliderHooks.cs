using System;
using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>
    /// Extension point for game-specific components on generated collider pieces. A character controller
    /// package subscribes and adds, for example, its own ice or water marker component to each piece.
    /// </summary>
    public static class ConvexColliderHooks
    {
        public struct Piece
        {
            /// <summary>The generated piece object carrying the collider.</summary>
            public GameObject gameObject;
            public ControllerSurface.Kind kind;
            public bool noFallDamage;
            /// <summary>True for water and trigger volumes (the collider is a trigger).</summary>
            public bool isTrigger;
            /// <summary>Name of the source brush.</summary>
            public string brushName;
        }

        /// <summary>Raised for every piece right after its collider was created.</summary>
        public static event Action<Piece> PieceCreated;

        public static void RaisePieceCreated(in Piece piece)
        {
            PieceCreated?.Invoke(piece);
        }
    }
}
