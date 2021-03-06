using SharpDX;
using WoWEditor6.Utils;

namespace WoWEditor6.Scene.Models.WMO
{
    class WmoInstance
    {
        private readonly Matrix mInstanceMatrix;

        public BoundingBox BoundingBox;

        public int Uuid { get; private set; }
        public BoundingBox[] GroupBoxes { get; private set; }
        public Matrix InstanceMatrix { get { return mInstanceMatrix; } }

        public WmoInstance(int uuid, Vector3 position, Vector3 rotation, WmoRootRender model)
        {
            Uuid = uuid;
            BoundingBox = model.BoundingBox;
            mInstanceMatrix = Matrix.RotationYawPitchRoll(MathUtil.DegreesToRadians(rotation.Y),
                                  MathUtil.DegreesToRadians(rotation.X), MathUtil.DegreesToRadians(rotation.Z)) * Matrix.Translation(position);

            BoundingBox = BoundingBox.Transform(ref mInstanceMatrix);
            GroupBoxes = new BoundingBox[model.Groups.Count];
            for(var i = 0; i < GroupBoxes.Length; ++i)
            {
                var group = model.Groups[i];
                GroupBoxes[i] = group.BoundingBox.Transform(ref mInstanceMatrix);
            }

            mInstanceMatrix = Matrix.Transpose(mInstanceMatrix);
        }


    }
}