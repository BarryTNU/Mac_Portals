using CommunityToolkit.Mvvm.Input;
using Portals.Models;

namespace Portals.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}