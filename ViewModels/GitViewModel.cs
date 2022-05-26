using LibGit2Sharp;

namespace rgit.ViewModels;

public class GitViewModel : ViewModelBase
{
    public GitViewModel(Repository repository)
    {
        this.Repository = repository;
    }

    public Repository Repository { get; }
}