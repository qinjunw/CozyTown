using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class T1NpcMovementAssetTests
    {
        [TestCase("shopkeeper_mina")]
        [TestCase("farmer_eli")]
        [TestCase("fisher_ren")]
        [TestCase("cook_sora")]
        public void MovementSheet_ImportsTwelveDirectionalSpritesAtNativeDimensions(string owner)
        {
            string path = "Assets/CozyTown/Art/Production/Characters/npc_" + owner + "_move_24x32.png";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(texture, Is.Not.Null, owner + " four-direction movement sheet is missing.");
            Assert.That(texture.width, Is.EqualTo(72));
            Assert.That(texture.height, Is.EqualTo(128));

            string[] names =
            {
                "idle_down", "walk_down_00", "walk_down_01",
                "idle_left", "walk_left_00", "walk_left_01",
                "idle_right", "walk_right_00", "walk_right_01",
                "idle_up", "walk_up_00", "walk_up_01"
            };
            names = names.Select(pose => "npc_" + owner + "_" + pose).ToArray();
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            Assert.That(sprites.Select(sprite => sprite.name), Is.EquivalentTo(names));
            for (int index = 0; index < names.Length; index++)
            {
                Sprite sprite = Array.Find(sprites, candidate => candidate.name == names[index]);
                Assert.That(sprite.rect, Is.EqualTo(new Rect(index % 3 * 24, (3 - index / 3) * 32, 24, 32)));
                Assert.That(sprite.pixelsPerUnit, Is.EqualTo(16));
                Assert.That(sprite.pivot, Is.EqualTo(new Vector2(12, 0)));
            }
        }
    }
}
