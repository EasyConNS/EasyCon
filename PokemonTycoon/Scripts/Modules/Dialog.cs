using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    internal class Dialog : ScriptCore
    {
        public bool CheckGetPokemon()
        {
            Log($"Dialog.CheckGetPokemon()");
            return VideoCapture.Match(410, 970, ImageRes.Get("dialog_getpokemon"), DefaultImageCap, VideoCapture.LineSampler(3));
        }
    }
}
