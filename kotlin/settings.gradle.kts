pluginManagement {
    repositories {
        gradlePluginPortal()
        mavenCentral()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories { mavenCentral() }
}

rootProject.name = "simplebehav"

include("simplebehav-dsl")
project(":simplebehav-dsl").projectDir = file("../DSL/kotlin")

include("simplebehav-playwright")
project(":simplebehav-playwright").projectDir = file("../Playwright/kotlin")

include("simplebehav-ktor")
project(":simplebehav-ktor").projectDir = file("../Ktor/kotlin")
