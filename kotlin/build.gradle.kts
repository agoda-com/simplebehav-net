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
        tasks.withType<KotlinCompile> {
            compilerOptions.jvmTarget.set(JvmTarget.JVM_11)
        }
        tasks.withType<Test> {
            useJUnitPlatform()
        }
    }
}
