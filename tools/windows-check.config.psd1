@{
    Solution = 'Pedia.sln'
    TestProjects = @(
        @{
            Path = 'tests\Pedia.Tests\Pedia.Tests.csproj'
            Configuration = 'Release'
            Properties = @{
                Platform = 'x64'
            }
        }
    )
    BuildProjects = @(
        @{
            Path = 'src\Pedia.App\Pedia.App.csproj'
            Configuration = 'Release'
            Properties = @{
                Platform = 'x64'
            }
        }
    )
}
