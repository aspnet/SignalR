@Library('dotnet-ci') _

// 'node' indicates to Jenkins that the enclosed block runs on a node that matches
// the label 'windows-with-vs'
simpleNode('Windows_NT','latest') {
    stage ('Checking out source') {
        checkout scm
    }
    stage ('Build') {
        def environment = 'set ASPNETCORE_TEST_LOG_DIR=.\testlogs'
        try
        {
            bat "${environment} & .\\run.cmd -CI default-build"
        }
        finally
        {
            archiveArtifacts artifacts: 'testlogs.zip', fingerprint: true
        }
    }
}
