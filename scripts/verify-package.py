"""Exercise the actual NuGet artifact in isolated consumer projects."""

import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as element_tree
import zipfile


def execute(arguments, directory, expected_diagnostic=None):
    result = subprocess.run(arguments, cwd=directory, capture_output=True, text=True, check=False)
    output = result.stdout + result.stderr
    if expected_diagnostic is None:
        if result.returncode != 0:
            raise RuntimeError(output)
    elif result.returncode == 0 or expected_diagnostic not in output:
        raise RuntimeError(f"Expected build failure containing {expected_diagnostic}:\n{output}")
    if "AD0001" in output:
        raise RuntimeError(f"An analyzer crashed:\n{output}")
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    arguments = parser.parse_args()
    package_path = arguments.package.resolve()
    development_kit = shutil.which("dotnet")
    if development_kit is None:
        raise RuntimeError("Install the .NET SDK from global.json and put dotnet on PATH.")

    with zipfile.ZipFile(package_path) as package_archive:
        package_files = package_archive.namelist()
        required_files = {
            "analyzers/dotnet/cs/NetAgents.Analyzers.dll",
            "buildTransitive/NetAgents.Analyzers.props",
            "buildTransitive/NetAgents.Analyzers.targets",
            "buildTransitive/NetAgents.globalconfig",
            "README.md",
            "LICENSE",
        }
        if not required_files.issubset(package_files):
            raise RuntimeError(f"Missing package files: {required_files.difference(package_files)}")
        assembly_files = [name for name in package_files if name.endswith(".dll")]
        if assembly_files != ["analyzers/dotnet/cs/NetAgents.Analyzers.dll"]:
            raise RuntimeError(f"Expected one analyzer assembly, received {assembly_files}")
        manifest = element_tree.fromstring(package_archive.read("NetAgents.Analyzers.nuspec"))
        package_version = manifest.findtext("{*}metadata/{*}version")
        if manifest.findall("{*}metadata/{*}dependencies/{*}group/{*}dependency"):
            raise RuntimeError("The analyzer package must not introduce runtime dependencies.")

    with tempfile.TemporaryDirectory(prefix="netagents-package-consumer-") as temporary_directory:
        consumer_directory = Path(temporary_directory)
        project_path = consumer_directory / "Consumer.csproj"
        source_path = consumer_directory / "ExampleService.cs"
        project_text = f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NetAgents.Analyzers" Version="{package_version}" PrivateAssets="all" />
  </ItemGroup>
</Project>
"""
        project_path.write_text(project_text, encoding="utf-8")
        (consumer_directory / "NuGet.Config").write_text(
            '<configuration><packageSources><clear /><add key="local" value="'
            + str(package_path.parent) + '" /></packageSources></configuration>', encoding="utf-8")
        valid_source = """namespace Consumer.Features;

public static class ExampleService
{
    public static string CreateGreeting(string recipientName)
    {
        return recipientName;
    }
}
"""
        source_path.write_text(valid_source, encoding="utf-8")
        # Use a private package cache to avoid accepting a stale artifact with the same version.
        execute([development_kit, "restore", str(project_path), "--packages", str(consumer_directory / "packages")], consumer_directory)
        build_command = [development_kit, "build", str(project_path), "--no-restore", "--nologo", "--verbosity", "quiet"]
        execute(build_command, consumer_directory)
        print("Valid consumer: passed")

        source_path.write_text(valid_source.replace("string CreateGreeting(string recipientName)", "System.IComparable CreateGreeting(int recipientName)"), encoding="utf-8")
        execute(build_command, consumer_directory, "NETAGENTS0001")
        print("Consumer boxing: rejected")
        source_path.write_text("#pragma warning disable NETAGENTS0001\n" + valid_source.replace("string CreateGreeting(string recipientName)", "System.IComparable CreateGreeting(int recipientName)"), encoding="utf-8")
        execute(build_command, consumer_directory, "NETAGENTS0001")
        print("Pragma suppression: rejected")
        source_path.write_text(valid_source.replace("        return recipientName;", "return recipientName;"), encoding="utf-8")
        execute(build_command, consumer_directory, "IDE0055")
        print("Consumer formatting violation: rejected")
        source_path.write_text(valid_source, encoding="utf-8")

        editor_configuration = consumer_directory / ".editorconfig"
        editor_configuration.write_text("root = true\n[*.cs]\ncsharp_style_var_elsewhere = true:silent\n", encoding="utf-8")
        execute(build_command, consumer_directory, "NETAGENTS0013")
        print("Consumer formatting override: rejected")
        editor_configuration.write_text("root = true\n[*.cs]\ndotnet_diagnostic.NETAGENTS0001.severity = none\n", encoding="utf-8")
        execute(build_command, consumer_directory, "NETAGENTS0013")
        print("Consumer severity override: rejected")
        editor_configuration.unlink()

        project_path.write_text(project_text.replace("<ImplicitUsings>enable</ImplicitUsings>", "<ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>false</TreatWarningsAsErrors>"), encoding="utf-8")
        execute(build_command, consumer_directory, "NETAGENTS0014")
        print("Consumer project override: rejected")
        project_path.write_text(project_text, encoding="utf-8")

        execute(build_command + ["-p:RunAnalyzers=false"], consumer_directory, "NETAGENTS0014")
        print("Disabled analyzers: rejected")
        execute(build_command + ["-p:NoWarn=NETAGENTS0001"], consumer_directory, "NETAGENTS0014")
        print("Project diagnostic suppression: rejected")
        execute(build_command, consumer_directory)
        print("Restored consumer: passed")


if __name__ == "__main__":
    main()
