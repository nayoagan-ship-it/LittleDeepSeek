using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace LittleDeepSeek
{
    internal sealed class SkinPoseDefinition
    {
        public string image { get; set; }
        public int[] rect { get; set; }
        public float height { get; set; }
    }
    internal sealed class SkinDefinition
    {
        public int schemaVersion { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public bool sideFacesLeft { get; set; }
        public float[] mouth { get; set; }
        public Dictionary<string, SkinPoseDefinition> poses { get; set; }
    }
    internal sealed class SkinPose
    {
        internal Bitmap Image;
        internal Rectangle Crop;
        internal float Height;
    }

    internal sealed class PetSkin : IDisposable
    {
        internal const string DefaultId = "default";
        internal static readonly string[] BasePoses = { "front", "side", "back", "sleep", "flower", "rice" };
        internal static readonly string[] RequiredPoses = { "front", "side", "back", "sleep", "flower", "rice", "blink", "stretch", "yawn", "think", "daydream", "note", "bubbles", "celebrate" };
        internal readonly string Id, Name;
        internal readonly Bitmap[] Images;
        internal readonly Rectangle[] Bounds;
        internal readonly Dictionary<string, SkinPose> Poses = new Dictionary<string, SkinPose>(StringComparer.Ordinal);
        private readonly bool nativeClips;
        internal bool UsesNativeClips { get { return nativeClips; } }
        internal readonly bool SideFacesLeft;
        internal float MouthX = .5f, MouthY = .46f;
        private bool disposed;

        internal static string NormalizeId(string id)
        {
            return id != null && Regex.IsMatch(id, @"^[a-z0-9][a-z0-9_-]{0,63}$") ? id : DefaultId;
        }

        internal PetSkin(string id, string name, Bitmap[] images, Rectangle[] bounds)
        {
            Id = id; Name = name; Images = images; Bounds = bounds;
            nativeClips = id == DefaultId;
            for (int i = 0; i < BasePoses.Length; i++)
                if (images[i] != null) Poses.Add(BasePoses[i], new SkinPose { Image = images[i], Crop = bounds[i], Height = 222 });
        }

        private PetSkin(SkinDefinition definition, Dictionary<string, SkinPose> poses)
        {
            Id = definition.id; Name = definition.name; Poses = poses;
            SideFacesLeft = definition.sideFacesLeft;
            Images = new Bitmap[BasePoses.Length]; Bounds = new Rectangle[BasePoses.Length];
            for (int i = 0; i < BasePoses.Length; i++) { Images[i] = poses[BasePoses[i]].Image; Bounds[i] = poses[BasePoses[i]].Crop; }
            if (definition.mouth != null && definition.mouth.Length == 2) { MouthX = definition.mouth[0]; MouthY = definition.mouth[1]; }
        }

        internal SkinPose Pose(string name)
        {
            SkinPose pose;
            if (!Poses.TryGetValue(name, out pose)) throw new InvalidDataException(Name + " 缺少动作姿态：" + name);
            return pose;
        }

        internal static Rectangle OpaqueBounds(Bitmap image)
        {
            int left = image.Width, top = image.Height, right = 0, bottom = 0;
            using (Bitmap argb = image.PixelFormat == PixelFormat.Format32bppArgb ? null : image.Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb))
            {
                Bitmap pixels = argb ?? image;
                BitmapData data = pixels.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[pixels.Width * 4];
                    for (int y = 0; y < pixels.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        for (int x = 0; x < pixels.Width; x++)
                            if (row[x * 4 + 3] > 8) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
                    }
                }
                finally { pixels.UnlockBits(data); }
            }
            if (right <= left || bottom <= top) throw new InvalidDataException("皮肤图片中没有可见角色。");
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        internal static PetSkin LoadDirectory(string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string json = File.ReadAllText(Path.Combine(root, "skin.json"));
            return Load(json, delegate(string file)
            {
                string path = Path.GetFullPath(Path.Combine(root, file));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("皮肤图片必须位于皮肤包内。");
                return File.OpenRead(path);
            });
        }

        internal static PetSkin LoadDefault()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("LittleDeepSeek.skin.default.json"))
            {
                // Keep the original static-art injection interface usable without a pose atlas.
                if (stream == null)
                {
                    string[] files = { "pet.png", "side.png", "back.png", "sleep.png", "flower.png", "rice.png" };
                    Bitmap[] images = new Bitmap[files.Length]; Rectangle[] bounds = new Rectangle[files.Length];
                    try
                    {
                        for (int i = 0; i < files.Length; i++)
                            using (Stream image = assembly.GetManifestResourceStream("LittleDeepSeek." + files[i]))
                            using (Bitmap original = new Bitmap(image))
                            { images[i] = new Bitmap(original); bounds[i] = OpaqueBounds(images[i]); }
                        return new PetSkin(DefaultId, "小deepseek", images, bounds);
                    }
                    catch { foreach (Bitmap image in images) if (image != null) image.Dispose(); throw; }
                }
                using (StreamReader reader = new StreamReader(stream))
                    return Load(reader.ReadToEnd(), delegate(string file)
                    {
                        Stream image = assembly.GetManifestResourceStream("LittleDeepSeek.skin.default." + file);
                        if (image == null) throw new FileNotFoundException("缺少小deepseek新默认动作素材。", file);
                        return image;
                    }, true);
            }
        }

        internal static PetSkin Load(string json, Func<string, Stream> openImage, bool allowDefault = false)
        {
            SkinDefinition definition = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 }.Deserialize<SkinDefinition>(json);
            if (definition == null || definition.schemaVersion != 1 || (definition.id == DefaultId && !allowDefault) || NormalizeId(definition.id) != definition.id || String.IsNullOrWhiteSpace(definition.name) || definition.name.Length > 32 || definition.poses == null)
                throw new InvalidDataException("皮肤定义无效。");
            foreach (string required in RequiredPoses)
                if (!definition.poses.ContainsKey(required)) throw new InvalidDataException(definition.name + " 缺少动作姿态：" + required);
            if (definition.mouth != null && (definition.mouth.Length != 2 || definition.mouth[0] < 0 || definition.mouth[0] > 1 || definition.mouth[1] < 0 || definition.mouth[1] > 1))
                throw new InvalidDataException("皮肤嘴部位置必须在角色范围内。");
            Dictionary<string, Bitmap> atlases = new Dictionary<string, Bitmap>();
            Dictionary<string, SkinPose> poses = new Dictionary<string, SkinPose>();
            try
            {
                foreach (string key in RequiredPoses)
                {
                    SkinPoseDefinition spec = definition.poses[key];
                    if (spec == null || String.IsNullOrWhiteSpace(spec.image) || spec.rect == null || spec.rect.Length != 4 || spec.height < 70 || spec.height > 260)
                        throw new InvalidDataException("动作姿态定义无效：" + key);
                    Bitmap atlas;
                    if (!atlases.TryGetValue(spec.image, out atlas))
                    {
                        using (Stream stream = openImage(spec.image))
                        using (Bitmap source = new Bitmap(stream))
                        {
                            if (source.Width > 8192 || source.Height > 8192) throw new InvalidDataException("皮肤图集过大。");
                            atlas = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
                        }
                        atlases.Add(spec.image, atlas);
                    }
                    Rectangle cell = new Rectangle(spec.rect[0], spec.rect[1], spec.rect[2], spec.rect[3]);
                    if (cell.Width < 2 || cell.Height < 2 || cell.X < 0 || cell.Y < 0 || cell.Right > atlas.Width || cell.Bottom > atlas.Height)
                        throw new InvalidDataException("动作姿态超出图集：" + key);
                    Bitmap frame = atlas.Clone(cell, PixelFormat.Format32bppArgb);
                    try
                    {
                        Rectangle crop = OpaqueBounds(frame);
                        if (crop.Left == 0 || crop.Top == 0 || crop.Right == frame.Width || crop.Bottom == frame.Height)
                            throw new InvalidDataException("动作姿态被图集边界裁切：" + key);
                        poses.Add(key, new SkinPose { Image = frame, Crop = crop, Height = spec.height });
                    }
                    catch { frame.Dispose(); throw; }
                }
                return new PetSkin(definition, poses);
            }
            catch { foreach (SkinPose pose in poses.Values) pose.Image.Dispose(); throw; }
            finally { foreach (Bitmap atlas in atlases.Values) atlas.Dispose(); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            HashSet<Bitmap> images = new HashSet<Bitmap>();
            foreach (SkinPose pose in Poses.Values) images.Add(pose.Image);
            foreach (Bitmap image in images) image.Dispose();
        }
    }
}
