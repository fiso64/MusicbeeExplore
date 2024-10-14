using System.Threading.Tasks;

namespace MusicBeePlugin.Commands
{
    public interface ICommand
    {
        Task Execute();
    }
}