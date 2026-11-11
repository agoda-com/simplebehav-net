import org.gradle.api.plugins.JavaPluginExtension
import org.gradle.api.tasks.compile.JavaCompile
import org.gradle.api.tasks.testing.Test
import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.tasks.KotlinCompile

plugins {
    kotlin("jvm") version "2.1.0" apply false
    kotlin("plugin.serialization") version "2.1.0" apply false
}

subprojects {
    group = "io.agoda.simplebehav"
    version = "0.1.0"

    plugins.withId("org.jetbrains.kotlin.jvm") {
        // CI runs on JDK 17 and a developer machine may not. Pin both compilers to 17.
        // Leaving Kotlin on 11 makes compileKotlin disagree with compileJava and fail the build.
        extensions.configure<JavaPluginExtension>("java") {
            sourceCompatibility = JavaVersion.VERSION_17
            targetCompatibility = JavaVersion.VERSION_17
        }
        tasks.withType<JavaCompile>().configureEach {
            options.release.set(17)
        }
        tasks.withType<KotlinCompile>().configureEach {
            compilerOptions.jvmTarget.set(JvmTarget.JVM_17)
        }
        tasks.withType<Test> {
            useJUnitPlatform()
        }
    }
}
