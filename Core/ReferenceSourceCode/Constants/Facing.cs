using System;
using System.Runtime.CompilerServices;
using Game.Rendering;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Constants {
    public static class Facing {
        public enum Type {
            Down = 0,
            Left = 1,
            Up = 2,
            Right = 3,
        }


        public static readonly Type[] Types = new Type[] {
            Type.Down, Type.Left, Type.Up, Type.Right
        };

        public static string IconFor(Type rot) {
            switch (rot) {
                case Type.Down: return IconId.WArrowDown;
                case Type.Up: return IconId.WArrowUp;
                case Type.Left: return IconId.WArrowLeft;
                case Type.Right: return IconId.WArrowRight;
            }
            return IconId.CMissing;
        }
        public static int UpOf(int pos, Type facing, int dist) {
            var xAdj = 0;
            var yAdj = 0;
            switch (facing) {
                case Type.Down:  yAdj = -1; break;
                case Type.Up:    yAdj =  1; break;
                case Type.Left:  xAdj = -1; break;
                case Type.Right: xAdj =  1; break;
            }
            return Pos.NeighborAt(pos, xAdj * dist, yAdj * dist);
        }
        public static int DownOf(int pos, Type facing, int dist) {
            var xAdj = 0;
            var yAdj = 0;
            switch (facing) {
                case Type.Down:  yAdj =  1; break;
                case Type.Up:    yAdj = -1; break;
                case Type.Left:  xAdj =  1; break;
                case Type.Right: xAdj = -1; break;
            }
            return Pos.NeighborAt(pos, xAdj * dist, yAdj * dist);
        }

        public static int LeftOf(int pos, Type facing, int dist) {
            var xAdj = 0;
            var yAdj = 0;
            switch (facing) {
                case Type.Down:  xAdj =  1; break;
                case Type.Up:    xAdj = -1; break;
                case Type.Left:  yAdj = -1; break;
                case Type.Right: yAdj =  1; break;
            }
            return Pos.NeighborAt(pos, xAdj * dist, yAdj * dist);
        }
        public static int RightOf(int pos, Type facing, int dist) {
            var xAdj = 0;
            var yAdj = 0;
            switch (facing) {
                case Type.Down:  xAdj = -1; break;
                case Type.Up:    xAdj =  1; break;
                case Type.Left:  yAdj =  1; break;
                case Type.Right: yAdj = -1; break;
            }
            return Pos.NeighborAt(pos, xAdj * dist, yAdj * dist);
        }

        public static int NeighborAt(int pos, Type facing) {
            var xAdj = 0;
            var yAdj = 0;
            switch (facing) {
                case Type.Down:  yAdj = -1; break;
                case Type.Up:    yAdj =  1; break;
                case Type.Left:  xAdj = -1; break;
                case Type.Right: xAdj =  1; break;
            }
            return Pos.NeighborAt(pos, xAdj, yAdj);
        }

        public static Type Rotate(Type cur, Type dir) {
            if (dir == Type.Right) {
                switch (cur) {
                    case Type.Down: return Type.Left;
                    case Type.Left: return Type.Up;
                    case Type.Up: return Type.Right;
                    case Type.Right: return Type.Down;
                }
            } else if (dir == Type.Left) {
                switch (cur) {
                    case Type.Down: return Type.Right;
                    case Type.Right: return Type.Up;
                    case Type.Up: return Type.Left;
                    case Type.Left: return Type.Down;
                }
            } else {
                // if we rotate up / down, we're rotating 2 steps in either direction,
                // so we get opposite
                return Opposite(cur);
            }
            return cur;
        }

        public static Type Rotate(Type currentRotation, int direction,
                Facing.Type[] validRots) {
            int firstNewRot = (4 + (int) currentRotation + direction) % 4;
            int newRot = firstNewRot;
            if (validRots != null && validRots.Length > 0) {
                for (int i = 0; i < 4; i++) {
                    if (Array.IndexOf(validRots, (Type) newRot) == -1) {
                        newRot = (4 + newRot + direction) % 4;
                    } else {
                        return (Type) newRot;
                    }
                }
                newRot = firstNewRot;
                D.Err("Rotating object that has no valid rotations defined!");
            }
            return (Type) newRot;
        }

        public static Quaternion ToQuaternion(Type rotation) {
            return rotation switch {
                Type.Up => Quaternion.Euler(0, 0, 180),
                Type.Down => Quaternion.identity,
                Type.Left => Quaternion.Euler(0, 0, -90),
                Type.Right => Quaternion.Euler(0, 0, 90),
                _ => Quaternion.identity
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRotated(Type type) {
            return type == Type.Left || type == Type.Right;
        }

        public static SpriteData.Flip ToFlip(Facing.Type rot) {
            switch (rot) {
                case Type.Up:
                case Type.Down: return SpriteData.Flip.Vertical;
                case Type.Left:
                case Type.Right: return SpriteData.Flip.Horizontal;
                default: {
                    D.Err( "Invalid facing: {0}", rot);
                    return SpriteData.Flip.None;
                }
            }
        }

        public static string FaceSpriteTo(string spriteId, Type facing) {
            switch (facing) {
                case Type.Up: return $"{spriteId}_U";
                case Type.Down: return $"{spriteId}_D";
                case Type.Left: return $"{spriteId}_L";
                case Type.Right: return $"{spriteId}_R";
            }
            D.Err("Invalid facing: {0}", facing);
            return spriteId;
        }

        public static Vector2 ToOffset(Type facing, float v) {
            switch (facing) {
                case Type.Down: return new(0, -v);
                case Type.Left: return new(-v, 0);
                case Type.Up: return new(0, v);
                case Type.Right: return new(v, 0);
                default: {
                    D.Err( "Invalid facing: {0}", facing);
                    return Vector2.zero;
                }
            }
        }
        public static float ToAngle(Type rot) {
            switch (rot) {
                case Type.Down: return 180f;
                case Type.Left: return 270f;
                case Type.Up: return 0f;
                case Type.Right: return 90f;
                default: {
                    D.Err( "Invalid facing: {0}", rot);
                    return 90f;
                }
            }
        }

        public static SpriteData.Rot ToRot(Type rot) {
            switch (rot) {
                case Type.Down: return SpriteData.Rot.Rot0;
                case Type.Left: return SpriteData.Rot.Rot90;
                case Type.Up: return SpriteData.Rot.Rot180;
                case Type.Right: return SpriteData.Rot.Rot270;
                default: {
                    D.Err( "Invalid facing: {0}", rot);
                    return SpriteData.Rot.Rot0;
                }
            }
        }

        public static Type ConvertRotation(char c) {
            switch (c) {
                case 'D' : return Type.Down;
                case 'R' : return Type.Right;
                case 'U' : return Type.Up;
                case 'L' : return Type.Left;
            }
            D.Err("Invalid rotation: {0}", c);
            return Type.Down;
        }

        public static bool CanSee(Vector2 from, Vector2 to, Type facingFrom) {
            var dir = to - from;
            var facingVec = ToOffset(facingFrom, 1);
            return Vector2.Dot(facingVec, dir) > 0;
        }

        public static char ConvertRotation(Type t) {
            switch (t) {
                case Type.Down: return 'D';
                case Type.Right: return 'R';
                case Type.Up: return 'U';
                case Type.Left: return 'L';
            }
            return 'D';
        }
        public static bool IsOpposite(Type a, Type b) {
            switch (a) {
                case Type.Down: return b == Type.Up;
                case Type.Left: return b == Type.Right;
                case Type.Up: return b == Type.Down;
                case Type.Right: return b == Type.Left;
                default: {
                    D.Err( "Invalid facing: {0}", a);
                    return false;
                }
            }
        }

        public static Type Opposite(Type currentRotation) {
            switch (currentRotation) {
                case Type.Down: return Type.Up;
                case Type.Left: return Type.Right;
                case Type.Up: return Type.Down;
                case Type.Right: return Type.Left;
                default: {
                    D.Err( "Invalid facing: {0}", currentRotation);
                    return Type.Down;
                }
            }
        }

        public static Type Mirrored(Type currentRotation) {
            switch (currentRotation) {
                case Type.Down: return Type.Left;
                case Type.Left: return Type.Down;
                case Type.Up: return Type.Right;
                case Type.Right: return Type.Up;
                default: {
                    D.Err( "Invalid facing: {0}", currentRotation);
                    return Type.Down;
                }
            }
        }
        public static Type FromDirection(Vector2 v, Type previous) {
            //return Type.Down;
            if (v.sqrMagnitude == 0) { return previous; }
            var aX = Math.Abs(v.x);
            var aY = Math.Abs(v.y);
            if (aX > aY) {
                // going horizontally
                return v.x > 0 ? Type.Right : Type.Left;
            } else {
                // going vertically
                return v.y > 0 ? Type.Up : Type.Down;
            }
        }

        public static Type FromAngle(float angle) {
            angle = angle % 360;
            if (angle < 0) angle += 360;

            if (angle >= 315 || angle < 45) return Type.Up;
            if (angle >= 45 && angle < 135) return Type.Right;
            if (angle >= 135 && angle < 225) return Type.Down;
            if (angle >= 225 && angle < 315) return Type.Left;

            D.Err("Invalid angle: {0}", angle);
            return Type.Down;
        }
    }
}
